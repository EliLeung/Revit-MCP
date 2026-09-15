# H2-Revit v1.0

首个公开版本，仅提供 Windows x64 / Revit 2026 安装包。

## 下载使用

下载 `H2-Revit-v1.0-Revit2026-win-x64.zip`，完整解压。关闭 Revit 后运行 `Install.cmd`。启动 Revit，在「附加模块 → H2-Revit → 连接管理」查看和管理连接。

MCP 服务程序随包提供；按 README 将安装器生成的 `codex-mcp.toml` 配置加入客户端。卸载运行 `Uninstall.cmd`。

## 新增

- 最多 16 个客户端连接。
- 连接列表、进程信息、备注、请求状态、自动刷新。
- 选择性断开、按进程及启动时间临时阻止和解除重连。
- 原版注册备份与恢复。

## 测试范围

编译、Windows 命名管道、连接管理、独立 WPF 窗口及隔离安装器测试通过。
两个独立发布版 MCP 客户端同时读取本机 Revit 视图成功。Revit 内新窗口的人工验证、模型写入及多个真实 Codex 聊天完整端到端验证尚未完成。
插件未代码签名；遵循所在组织的插件加载政策。

基于 bimwright/rvt-mcp 0.6.1，Apache-2.0；保留原作者 Khoa Le 归属。附第三方许可与 SHA-256 校验文件。
