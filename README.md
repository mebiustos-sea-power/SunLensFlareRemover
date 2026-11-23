# Sun Lens Flare Remover

Version **0.1.0** / Plugin ID: `mebiustos.sunlensflareremover`

太陽に付くレンズフレアを無効化するAnchor Chain用MODです。星状に伸びる光芒やレンズ内の反射像を対象にし、太陽の円盤を表示するマテリアル、照明、夕焼け、海面の反射、月のフレアには変更を加えません。設定ファイルは不要です。

## 導入

1. Anchor Chainと、Anchor Chainが必要とするBepInExプリローダーを導入してください。
2. ゲームを終了し、`dist/SunLensFlareRemover`フォルダを`Sea Power_Data/StreamingAssets`へコピーしてください。
3. MODマネージャーでAnchor ChainとSun Lens Flare Removerを有効にし、ゲームを再起動してください。

無効化する場合はMODマネージャーでチェックを外し、再起動してください。アンインストールする場合はゲーム終了後に配置したフォルダを削除してください。元ゲームのDLLやenvironment.iniの編集は不要です。

## ビルド

Windowsの.NET SDKを使用します。MOD本体はnetstandard2.1、検証プログラムはnet10.0を対象にします。NuGetパッケージのダウンロードは行いません。

`Directory.Build.local.props.example`を`Directory.Build.local.props`へコピーして、ゲームと実際に使用しているAnchorChain.dllのフォルダを設定してください。パスを引数で渡すこともできます。

```powershell
.\build.ps1 -Test
```

出力は`dist/SunLensFlareRemover`です。ビルドスクリプトはゲームへの配置や起動を行いません。

## 実装と検証範囲

`DynamicSky.updateFlares`にHarmonyのpostfixを適用し、太陽のLight.flareだけをnullにします。ゲームは空の更新時にフレアを再設定するため、更新後の除去を維持します。太陽用のフレアアセット自体と月側の処理は変更しません。ゲームのMOD有効状態を確認してからパッチを適用し、初期化失敗はログへ出してこのMODのパッチを解除します。ログ名はSun Lens Flare Removerです。

検証プログラムは、インストール済みゲームの実際のupdateFlaresとビルド済みMODのpostfixのILを読み込み、UnityのLight・カメラ等の境界だけを管理オブジェクトに置き換えて実行します。昼・夜・水中・高高度雲の条件、繰り返し更新、フレアアセットと照明値の保持、除去処理を適用しない場合の復帰を検証します。Harmonyが注入するフィールドとコールバック引数の型の適合も確認します。

この検証はUnity内でのパッチ適用・描画を実行するものではありません。太陽の見え方や海面の反射を含む実画面の比較は、ゲーム内での確認項目です。
