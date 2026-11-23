using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SunLensFlareRemover;
using EmitOpCode = System.Reflection.Emit.OpCode;

internal static class Program
{
    private static int checks;
    private static readonly Dictionary<short, EmitOpCode> OpCodesByValue = typeof(System.Reflection.Emit.OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (EmitOpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass the game assembly and built mod assembly.");
            using var game = AssemblyDefinition.ReadAssembly(args[0]);
            using var mod = AssemblyDefinition.ReadAssembly(args[1]);
            var skyType = game.MainModule.GetType("SeaPower.DynamicSky")
                ?? throw new InvalidDataException("The installed game has no DynamicSky.");
            var original = skyType.Methods.Single(method => method.Name == "updateFlares");
            var callback = mod.MainModule.GetType("SunLensFlareRemover.SunFlarePatch").Methods
                .Single(method => method.Name == "RemoveSunFlare");

            Check("postfix field injection matches the installed game's instance field", () =>
            {
                Require(!original.IsStatic && original.Parameters.Count == 0 && original.ReturnType.FullName == "System.Void", "Unexpected sky-update signature.");
                Require(callback.IsStatic && callback.Parameters.Count == 1, "Unexpected postfix signature.");
                var parameter = callback.Parameters[0];
                Require(parameter.Name.StartsWith("___", StringComparison.Ordinal), "The postfix does not request a field injection.");
                var field = skyType.Fields.SingleOrDefault(value => value.Name == parameter.Name.Substring(3))
                    ?? throw new MissingFieldException(skyType.FullName, parameter.Name.Substring(3));
                Require(!field.IsStatic && field.FieldType.FullName == parameter.ParameterType.FullName, "The injected field type is incompatible.");
            });

            // Execute the installed game's IL and the built production callback, replacing
            // only Unity/scene boundaries. No copy of the game's day/night logic is used.
            var update = Compile<Action<SkyFixture>>(original, typeof(SkyFixture));
            var remove = Compile<Action<LightFixture>>(callback, typeof(LightFixture));

            Check("unmodified daylight update assigns the sun asset and positions both discs", () =>
            {
                var sky = NewSky();
                update(sky);
                Require(ReferenceEquals(sky._sunLight.Flare, sky._sunFlare), "The baseline did not restore the sun flare.");
                Require(sky.Positions.Count == 2, "A celestial-disc position update was lost.");
                Require(ReferenceEquals(sky.Positions[0].Flare, sky._sunFlareTransform), "The sun disc was not positioned.");
                Require(ReferenceEquals(sky.Positions[1].Flare, sky._moonFlareTransform), "The moon disc was not positioned.");
            });
            Check("daylight removal preserves moon flare, source assets, and light values", () =>
            {
                var sky = NewSky();
                sky._moonLight.Flare = sky._moonFlare;
                var sunAsset = sky._sunFlare;
                update(sky);
                remove(sky._sunLight);
                Require(sky._sunLight.Flare == null, "The sun flare is still attached.");
                Require(ReferenceEquals(sky._moonLight.Flare, sky._moonFlare), "The moon flare changed.");
                Require(ReferenceEquals(sky._sunFlare, sunAsset), "The sun's source asset was modified.");
                Require(sky.Positions.Count == 2, "A disc update was suppressed.");
                CheckLightValues(sky);
            });
            Check("night update preserves the game's moon flare while clearing stale sun flare", () =>
            {
                var sky = NewSky();
                sky._sunAltitude = -10;
                sky._sunLight.Flare = sky._sunFlare;
                update(sky);
                remove(sky._sunLight);
                Require(sky._sunLight.Flare == null, "A previous sun flare survived at night.");
                Require(ReferenceEquals(sky._moonLight.Flare, sky._moonFlare), "The game's night-time moon flare was lost.");
                CheckLightValues(sky);
            });
            Check("underwater suppression of both flares remains in effect", () =>
            {
                var sky = NewSky();
                CameraFixture.Instance._isCameraUnderWater = true;
                sky._moonLight.Flare = sky._moonFlare;
                update(sky);
                remove(sky._sunLight);
                Require(sky._sunLight.Flare == null && sky._moonLight.Flare == null, "The game's underwater suppression changed.");
                CheckLightValues(sky);
            });
            Check("high-altitude-cloud suppression remains in effect", () =>
            {
                var sky = NewSky();
                sky.AboveHighAltCloud = true;
                sky._moonLight.Flare = sky._moonFlare;
                update(sky);
                remove(sky._sunLight);
                Require(sky._sunLight.Flare == null && sky._moonLight.Flare == null, "The game's high-altitude suppression changed.");
                CheckLightValues(sky);
            });
            Check("repeated sky updates cannot leave the sun flare restored", () =>
            {
                var sky = NewSky();
                for (var index = 0; index < 5; index++)
                {
                    update(sky);
                    Require(ReferenceEquals(sky._sunLight.Flare, sky._sunFlare), "The baseline stopped restoring the flare.");
                    remove(sky._sunLight);
                    Require(sky._sunLight.Flare == null, "The flare returned after a later update.");
                }
                Require(sky.Positions.Count == 10, "Repeated disc positioning was interrupted.");
            });
            Check("the original flare can return when the removal callback is not applied", () =>
            {
                var sky = NewSky();
                update(sky);
                remove(sky._sunLight);
                update(sky);
                Require(ReferenceEquals(sky._sunLight.Flare, sky._sunFlare), "The source flare was permanently lost.");
            });

            string package = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "activation-fixture");
            Check("checked package is enabled", () =>
            {
                FileManagerFixture.Instance.Directories.Clear();
                FileManagerFixture.Instance.Directories.Add(new DirectoryFixture(package, true));
                Require(ModActivation.IsEnabled(typeof(FileManagerFixture), package), "A checked package was rejected.");
            });
            Check("unchecked package remains disabled despite other enabled packages", () =>
            {
                FileManagerFixture.Instance.Directories.Clear();
                FileManagerFixture.Instance.Directories.Add(new DirectoryFixture(package + "-other", true));
                FileManagerFixture.Instance.Directories.Add(new DirectoryFixture(package, false));
                Require(!ModActivation.IsEnabled(typeof(FileManagerFixture), package), "An unchecked package was enabled.");
            });
            Check("an unregistered package is reported as an error", () =>
            {
                FileManagerFixture.Instance.Directories.Clear();
                try { ModActivation.IsEnabled(typeof(FileManagerFixture), package); }
                catch (InvalidOperationException) { return; }
                throw new InvalidOperationException("A missing mod registration was silently accepted.");
            });
            Console.WriteLine("All " + checks + " checks passed. Unity rendering and live Harmony installation were not exercised.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SkyFixture NewSky()
    {
        CameraFixture.Instance._isCameraUnderWater = false;
        return new SkyFixture();
    }

    private static void CheckLightValues(SkyFixture sky)
    {
        Require(sky._sunLight.Enabled && sky._moonLight.Enabled, "A light was disabled.");
        Require(sky._sunLight.Intensity == 1.1f && sky._moonLight.Intensity == 0.15f, "Scene illumination changed.");
        Require(sky._sunLight.Color == "sun" && sky._moonLight.Color == "moon", "A light's color changed.");
    }

    private static void Check(string name, Action test)
    {
        test();
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Compile<T>(MethodDefinition source, params Type[] parameters) where T : Delegate
    {
        if (!source.HasBody || source.Body.HasExceptionHandlers || source.Body.Variables.Count != 0)
            throw new NotSupportedException("The managed fixture does not support this method layout: " + source.FullName);
        var method = new DynamicMethod(source.Name + "_ManagedBoundary", typeof(void), parameters, typeof(Program).Module, true);
        var il = method.GetILGenerator();
        var labels = source.Body.Instructions.ToDictionary(instruction => instruction, _ => il.DefineLabel());
        foreach (var instruction in source.Body.Instructions)
        {
            il.MarkLabel(labels[instruction]);
            var code = OpCodesByValue[instruction.OpCode.Value];
            switch (instruction.Operand)
            {
                case null: il.Emit(code); break;
                case Instruction branch: il.Emit(code, labels[branch]); break;
                case FieldReference field: il.Emit(code, MapField(field)); break;
                case MethodReference called: il.Emit(code, MapMethod(called)); break;
                case float value: il.Emit(code, value); break;
                case int value: il.Emit(code, value); break;
                case sbyte value: il.Emit(code, value); break;
                default: throw new NotSupportedException("Unmapped engine boundary: " + instruction);
            }
        }
        return (T)method.CreateDelegate(typeof(T));
    }

    private static FieldInfo MapField(FieldReference source)
    {
        Type type = source.DeclaringType.FullName switch
        {
            "SeaPower.DynamicSky" => typeof(SkyFixture),
            "SeaPower.CameraManager" => typeof(CameraFixture),
            _ => throw new NotSupportedException("Unmapped field: " + source.FullName)
        };
        return type.GetField(source.Name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, source.Name);
    }

    private static MethodInfo MapMethod(MethodReference source)
    {
        if (source.DeclaringType.FullName == "UnityEngine.Light" && source.Name == "set_flare")
            return typeof(LightFixture).GetProperty(nameof(LightFixture.Flare))!.SetMethod!;
        if (source.DeclaringType.FullName == "SeaPower.DynamicSky" && source.Name == "updateFlarePosition")
            return typeof(SkyFixture).GetMethod(nameof(SkyFixture.UpdateFlarePosition))!;
        if (source.DeclaringType.FullName == "SeaPower.DynamicSky" && source.Name == "isCameraAboveHighAltCloud")
            return typeof(SkyFixture).GetMethod(nameof(SkyFixture.IsCameraAboveHighAltCloud))!;
        if (source.Name == "get_Instance" && source.DeclaringType is GenericInstanceType generic
            && generic.ElementType.FullName == "SeaPower.Singleton`1"
            && generic.GenericArguments.Single().FullName == "SeaPower.CameraManager")
            return typeof(CameraFixture).GetProperty(nameof(CameraFixture.Instance))!.GetMethod!;
        throw new NotSupportedException("Unmapped call: " + source.FullName);
    }

    public sealed class LightFixture
    {
        public object? Flare { get; set; }
        public bool Enabled = true;
        public float Intensity;
        public string Color = "";
    }

    public sealed class CameraFixture
    {
        public static CameraFixture Instance { get; } = new CameraFixture();
        public bool _isCameraUnderWater = false;
    }

    public sealed class SkyFixture
    {
        public readonly object _sunLightTransform = new object();
        public readonly object _sunFlareTransform = new object();
        public readonly object _moonLightTransform = new object();
        public readonly object _moonFlareTransform = new object();
        public readonly object _sunFlare = new object();
        public readonly object _moonFlare = new object();
        public readonly LightFixture _sunLight = new LightFixture { Intensity = 1.1f, Color = "sun" };
        public readonly LightFixture _moonLight = new LightFixture { Intensity = 0.15f, Color = "moon" };
        public float _sunAltitude = 10;
        public bool AboveHighAltCloud = false;
        public List<(object Light, object Flare, float Distance)> Positions { get; } = new();
        public void UpdateFlarePosition(object light, object flare, float distance) => Positions.Add((light, flare, distance));
        public bool IsCameraAboveHighAltCloud(float offset) => AboveHighAltCloud;
    }

    public sealed class FileManagerFixture
    {
        public static FileManagerFixture Instance { get; } = new FileManagerFixture();
        public List<DirectoryFixture> Directories { get; } = new();
    }

    public sealed class DirectoryFixture
    {
        public DirectoryInfo DirectoryInfo { get; }
        public bool IsChecked { get; }
        public DirectoryFixture(string path, bool enabled) { DirectoryInfo = new DirectoryInfo(path); IsChecked = enabled; }
    }
}
