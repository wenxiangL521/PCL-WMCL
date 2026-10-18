# 陶瓦联机 E2E 实测记录

日期：2026-10-19  
环境：Linux x86_64，Terracotta v0.4.2，两个独立 `TMPDIR` 的 Terracotta 进程  
用途：验证 `host-ok → guest-ok` 与客人端端口转发，不包含真实 Minecraft 客户端画面。

## 复现步骤

1. 准备 Terracotta Linux 二进制，并准备一个假的 Minecraft 局域网世界端口（25565）：
   - 该端口只需能接受 TCP 连接并返回 `0xFF` 开头的旧版响应；
2. 用不同 `TMPDIR` 启动两个 Terracotta 实例，拿到各自的 HMCL 端口文件：

```bash
TMPDIR=/tmp/tc-host terracotta --hmcl /tmp/tc-h.port
TMPDIR=/tmp/tc-guest terracotta --hmcl /tmp/tc-g.port
```

3. 房主调用：

```bash
curl 'http://127.0.0.1:<host-port>/state/scanning'
```

4. 向 Terracotta 的局域网扫描多播地址发送公告：

```text
[MOTD]PCL E2E[/MOTD][AD]25565[/AD]  → 224.0.2.60:4445
```

5. 等待房主状态：

```json
{"state":"host-ok","room":"U/Z2PH-UTV0-MH5K-THMR",...}
```

6. 客人调用：

```bash
curl 'http://127.0.0.1:<guest-port>/state/guesting?room=U%2FZ2PH-UTV0-MH5K-THMR'
```

7. 等待客人状态：

```json
{"state":"guest-ok","url":"127.0.0.1:39847",...}
```

8. 连接客人转发地址：

```text
127.0.0.1:39847 → 收到 0xFF 开头的 Minecraft 响应
```

## 实测结果

- 房主：`host-ok`，房间 `U/Z2PH-UTV0-MH5K-THMR`
- 客人：`guest-ok`，地址 `127.0.0.1:39847`
- 客人端口转发可连，收到假服务器响应
- 说明 Terracotta 的房间协议、房主扫描、客人加入、端口转发链路可用

## 未覆盖

- 真实 Minecraft 客户端进入世界；
- 两台物理机器/跨公网；
- 公共节点不可达、NAT 极端情况；
- 真实玩家的长时间稳定性。

这些需要你本地双机实测；若失败，附上两端 `TerracottaStatus` 与 `/tmp/terracotta/*/application.log`。
