> 这个文件自2026/9/25起不再更新
# XAML Sub — Packaged 重建说明

笑点解析：项目创成Unpackaged了，然后雷霆MsintX直接重新新建一个Packaged项目

## 关键点

- `EnableMsixTooling=true`
- `WindowsPackageType=MSIX`
- `AppxPackage=true`
- 项目根目录包含 `Package.appxmanifest`
- `Properties/launchSettings.json` 使用 `commandName: MsixPackage`
- x64 Debug / Release 都在 Solution Configuration 中启用 Build + Deploy
- `PublishProfile` 指向 `Properties\\PublishProfiles\\win10-$(Platform).pubxml`
- 保留现有 Designer 代码、Roslyn、工具箱、Tip、About Dialog、WindowTitleBar Preview 等功能
- 不使用 `WindowsPackageType=None`
- 不使用 `WindowsAppSDKSelfContained=true` 作为默认分发路线

## 开发证书

项目附带 `XamlSub_TemporaryKey.pfx`，用于本地开发/调试签名。

如果 Windows 在安装开发包时提示不信任发布者，请安装该 PFX 对应的证书到当前用户的“受信任的人”证书存储，或在 Visual Studio 的包签名设置中选择自己的开发证书。

## Visual Studio 2026

打开 `.sln` 后，顶部启动配置应该出现：

`XAML Sub`

它来自 `Properties/launchSettings.json` 中的 `MsixPackage` profile。

F5 的流程应为：

`Build → MSIX → Deploy → Launch → Debug`

如果“Configuration Manager”中显示 Deploy，请确保 Debug|x64 / Release|x64 对本项目均已勾选 Deploy。


### WMC0035
若 Visual Studio 报 `Duplication assignment to the 'Children' property of the 'Grid' object`，原因是 `Grid.KeyboardAccelerators` 的属性元素写法在当前 WinUI XAML 编译器中被错误解析。现已移至代码后置注册。
