# Testing

## 自动化测试

测试运行器位于：

```text
tests/DiskGrowthMonitor.TestRunner
```

运行：

```powershell
dotnet run --project tests\DiskGrowthMonitor.TestRunner\DiskGrowthMonitor.TestRunner.csproj
```

覆盖内容：

- 安全路径校验。
- 增长计算与目录聚合。
- SQLite 持久化。
- 目录累计阈值。
- 减少变化聚合。
- 溯源树。
- 忽略列表。
- 暂停恢复。
- 监听端到端。
- UI 烟雾测试。
- 源码危险调用扫描。

## 测试数据

自动化测试只使用仓库内 `.test-data` 临时目录。该目录被 `.gitignore` 永久忽略，不能提交。

测试不会：

- 关机。
- 断网。
- 修改网络代理。
- 写入真实个人目录的大文件。
- 删除或移动用户文件。

## 人工验收建议

1. 双击根目录 `DiskGrowthMonitor.App.exe`。
2. 确认主窗口显示，托盘图标存在。
3. 调整显示阈值，观察排行刷新。
4. 在普通测试目录中创建或追加文件，观察增长记录。
5. 删除或缩小测试文件，观察减少记录。
6. 点击排行条目，查看详情和溯源树。
