# SQLite → PostgreSQL 迁移记录

> 第③步：把三个服务（Family/Vault/AI）的数据库从 SQLite 换成 PostgreSQL（一服务一数据库的最终形态）。

## 现状（2026-08-19 已部署）

- **k8s**：`bh-postgres`（postgres:16-alpine，PVC hostPath `/opt/baihua/postgres`，Secret `baihua-pg-secret`），
  Service `bh-postgres:5432`；三个库 `family` / `vault` / `ai`（一服务一库）
- **连接**：`Baihua.Data.DbConnections.For(dbName)` 读 `PG_HOST`/`PG_USER`（configmap）+
  `PG_PASSWORD`（Secret `baihua-secret`），缺省 localhost/baihua
- **EF Core**：全部 DbContext 改用 `UseNpgsql`（Npgsql.EntityFrameworkCore.PostgreSQL 9.0.0）；
  SQLite 包引用已移除
- **建库方式**：`EnsureCreated`（无迁移历史；未来 schema 演进需生成 Npgsql 初始迁移后再 Migrate）
- **Migrations 目录已删除**（SQLite 迁移在 PG 下无效）；`SqliteSetup`(WAL)/`ClearAllPools`/
  `SQLitePCL.Batteries_V2.Init`/`sqlite_master`/裸 SQL 建表兜底（ServerAddressService）全部移除

## 数据迁移

脚本 `/tmp/pg_migrate.py`（SQLite → psql COPY）：family 16 行、vault 0 行、ai 30073 行（含
AiUsageMetrics 30062、AiProviderSettings 3）已导入；**各表序列已 setval 同步**（否则主键冲突 23505）。

## 回归结果（全部通过）

- 测速 Family→shim→deepseek 20~29 tok/s，新记录写 PG（family.BenchmarkSessions）
- 聊天（pool 网关）、capabilities、AI 配置（deepseek key 90 字符加密密文在 PG）
- 三服务日志 0 个 SQLite/Postgres 异常；pod 稳定

## 已知事项 / 待办

- **native 环境（Windows 寻芳居 .9 等）**：新代码连 PG，native 机器需部署 PostgreSQL
  （Windows 装 PG 服务或 Docker PG），并用 `ConnectionStrings__Family/Vault/AI` 或
  `PG_HOST/PG_USER/PG_PASSWORD` 环境变量指向；`tools/bh` 的 native 启动脚本需补充 PG 配置说明。
- SQLite 旧文件（/opt/baihua/data/db/*.db）保留作存档，不再被读写。
- 未来 schema 演进：需用 dotnet-ef 生成 Npgsql 迁移（EnsureCreated 不支持增量变更）。
- 备份：BackupService 用 EF JSON 导出（provider 无关），PG 下无需改造。
