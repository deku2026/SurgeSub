# SurgeSub

为 Surge Mac 生成本机专用配置。服务器提供分流、DNS、应用规则和原生节点，SurgeSub 在本机写入家宽／非家宽网卡与用户选择的本地代理入口。

需要 macOS 13+、Surge Mac 6.10+，并启用 Surge 增强模式。只支持 SubVault v1 JSON 配置包；节点目前支持服务器已校验的原生 Shadowsocks，不做 Clash 协议转换。

## 使用

从 [GitHub Releases](https://github.com/deku2026/SurgeSub/releases/latest) 下载：Apple Silicon 选择 `osx-arm64.dmg`，Intel Mac 选择 `osx-x64.dmg`。打开 DMG，将应用拖到 Applications；ZIP 也可直接解压使用。对应 `.sha256` 文件可用 `shasum -a 256 -c 文件名.sha256` 验证。

1. 将构建好的 `SurgeSub.app` 放进固定位置（建议 `/Applications`），然后打开。应用常驻 macOS 顶部菜单栏；首次使用打开设置，已有配置时仅显示图标。点击或右键图标可打开设置、立即同步或退出。
2. 粘贴 SubVault 的 `/surgemac/combined?token=…`、`/surgeios/combined?token=…` 或 `/surgemac/client-bundle?token=…` 链接。前两种地址会被转换成配置包接口。
3. **手动选择**家宽、非家宽网卡，填写本地 SOCKS5 端口，选择代理 App 或实际核心可执行文件。没有预选端口或 App；缺少任何必填项不能生成。
   网卡列表默认只显示已连接且具有可用 IPv4 出口的设备；需要时勾选“显示未连接网卡”。隐藏断开的网卡不会替换已保存的设备身份。
4. 启动所选代理应用，启用免认证 SOCKS5 或混合端口。应用检查监听进程归属并完成 SOCKS5 握手。核心位于 `.app` 外部时，选择该核心文件。
5. 点击“生成并应用到 Surge”。校验成功后切换到 `SurgeSub` 配置，并承接仍然存在的手动分组选择。更新可能中断连接，**自动同步默认关闭**；启动程序、打开或关闭设置窗口都不会更新配置。

Surge 负责增强模式接管；另一个代理应用仅提供本地端口，关闭它的 TUN 接管和系统代理接管。其进程规则优先于普通 DNS、应用、域名分流，固定经非家宽接口连接自己的上游。

## 更新与故障处理

- 菜单“立即同步”使用已保存的设置更新一次，不启用或恢复自动同步；编辑尚未保存的设置时，使用窗口中的“生成并应用到 Surge”。
- 主动勾选“自动同步”并应用后，每 30 秒检查网卡、端口和设置；按服务器间隔（当前 20 分钟）使用 ETag 检查订阅。
- 订阅下载通过明确选择并验证过的本地 SOCKS5 端口，避免首次更新受旧 Surge 出口设置影响；代理不可用时保留缓存。
- 网卡变化先使用本地缓存生成，不依赖网络恢复后才能修正出口。保存网络服务身份，重新查询接口号；无法确认身份时阻断对应出口，要求重新选择，绝不猜测另一台设备。
- 本地代理未运行或端口被其他程序占用时，后台将本地代理策略停用；恢复验证后重新启用。
- 所有机场节点直接写入非家宽 `interface`，不增加代理链。保留测速和 DNS 参数。
- 新版本资源在独立目录写全并校验后，原子替换主配置；Surge 重载完成后核对接口和节点资源状态。失败恢复上次主配置。自动同步时没有变化不重载，手动应用会核对运行中的配置。
- 下载失败保留旧数据。主配置由 SurgeSub 管理，不包含指向远端原始配置的 `#!MANAGED-CONFIG`。
- 节点仍使用独立本地 `policy-path` 文件；为避免旧缓存，每一代使用独立资源路径，因此节点变化会更新主配置中的资源引用。
- 关闭设置窗口只隐藏窗口。“退出 SurgeSub”停止本次后台同步并移除菜单栏图标，保留 Surge 正在使用的配置；再次打开程序不会自行恢复同步，需要保持勾选“自动同步”并再次应用。
- “登录后显示图标”与自动同步独立，勾选并应用后通过用户级 launchd 注册。仅显示图标不会同步；同时启用自动同步时才会在下次登录启动后台任务。未启用登录启动的后台任务仅在本次登录有效。
- 后台不会切换用户主动选择的其他 Surge 配置；会准备最新本地文件，手动应用时才切换。

设置、缓存、日志与备份：`~/Library/Application Support/SurgeSub/`。配置文件：`~/Library/Application Support/Surge/Profiles/SurgeSub.conf`。订阅和节点凭据仅保存于权限受限的本地文件，日志不输出订阅地址或节点密码。

## 开发与构建

```sh
dotnet test tests/SurgeSub.Tests/SurgeSub.Tests.csproj -c Release
dotnet build src/SurgeSub.App/SurgeSub.App.csproj -c Debug
python3 scripts/package-macos.py
```

使用 .NET 10、Avalonia 11.3.12 的 macOS 原生后端和 NativeAOT。后台独立为 `SurgeSub.Agent`，不加载 UI。构建输出在 `artifacts/`，不自动安装、修改当前 Surge 配置或注册登录任务。当前包采用本机临时签名，尚未做 Developer ID 签名和公证。

若 macOS 阻止首次打开，核对下载来源后，到“系统设置 → 隐私与安全性 → 仍要打开”。更新已有安装时，先退出界面、替换 App，再打开并点击“生成并应用”，使后台任务加载新版本。

CI 在 PR 和 main 上测试并构建 Apple Silicon / Intel 的 DMG、ZIP 与 SHA-256 文件，挂载 DMG 核验版本、架构、签名和后台组件启动后上传构建产物。发布时先修改 `VERSION` 并合并，随后为已合并提交推送对应的 `vX.Y.Z` 标签；标签与 `VERSION` 不一致会失败。全部检查通过后自动创建 GitHub Release，上传完整资源后才公开发布，失败的上传保留为草稿以便重试。构建产物内记录版本、CI 构建号和源代码提交。

跨仓库验证可在 SubVault 测试时设置 `SUBVAULT_CLIENT_FIXTURE=/tmp/client.json`，再以同样环境变量运行本仓库测试。本机安装 Surge 时额外执行原生语法校验；CI 无 Surge 授权时仅执行生成器、网络契约和事务测试。

协议说明见 [docs/client-bundle-v1.md](docs/client-bundle-v1.md)。
