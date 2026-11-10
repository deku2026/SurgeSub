# SubVault → SurgeSub v1

GET `/surgemac/client-bundle?token=…`，订阅 token 鉴权，HTTPS。普通 iOS 输出保持原样。响应为 JSON，不可直接导入 Surge。

```json
{
  "schemaVersion": 1,
  "revision": "opaque-revision",
  "refreshIntervalSeconds": 1200,
  "bindings": {
    "homePolicy": "家宽网卡直连",
    "otherPolicy": "非家宽网卡直连",
    "localProxyPolicy": "本地代理 · 非家宽"
  },
  "sections": [
    {"name":"Proxy","lines":["家宽网卡直连 = direct","非家宽网卡直连 = direct"]},
    {"name":"Proxy Group","lines":["Global = select, 家宽网卡直连, 非家宽网卡直连, 本地代理 · 非家宽, policy-path=surgesub-resource:all.conf"]}
  ],
  "nodeResources": [
    {"id":"all.conf","policies":["Example = ss, 192.0.2.1, 443, encrypt-method=aes-128-gcm, password=example-only"]}
  ]
}
```

示例省略了实际必需的 General、Rule、Host 等段。`sections` 保留服务器 DSL 编译后的 Surge 语义，但明确移除设备绑定和固定本地代理声明；`nodeResources` 与所有段来自同一不可变快照。每个资源必须非空且 ID 唯一，支持 `all.conf` 和 SHA256 分组 ID。

本地只补出口和本地代理，不重新排序原有域名／IP 分流。所选代理应用进程规则插入 Rule 首位。所有节点资源和两条 direct 别名都以 `allow-other-interface=false` 绑定本地选择的接口。

ETag 针对完整响应字节计算，`Cache-Control: private, no-cache`。304 使用同订阅地址下已验证的缓存，401/503/错误数据不能替换现有配置。换地址或换 token 不复用旧缓存。

`refreshIntervalSeconds` 由客户端调度；本地 `policy-path` 的更新由客户端执行，Surge 的 URL 更新间隔不适用于本地文件。
