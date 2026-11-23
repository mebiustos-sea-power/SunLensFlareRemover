using System;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SunLensFlareRemover;

internal static class SunFlarePatch
{
    internal static void Install(Harmony harmony)
    {
        var target = AccessTools.DeclaredMethod(typeof(DynamicSky), "updateFlares", Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(DynamicSky).FullName, "updateFlares");
        var sunLight = AccessTools.DeclaredField(typeof(DynamicSky), "_sunLight")
            ?? throw new MissingFieldException(typeof(DynamicSky).FullName, "_sunLight");
        if (sunLight.IsStatic || sunLight.FieldType != typeof(Light))
            throw new InvalidOperationException("DynamicSky._sunLight is not an instance Light field.");
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(SunFlarePatch), nameof(RemoveSunFlare)));
    }

    // The game restores this flare during sky updates; remove it after each update.
    // Leave its sun-disc material, illumination, reflections, and moon flare alone.
    private static void RemoveSunFlare(Light ____sunLight)
    {
        ____sunLight.flare = null;
    }
}
