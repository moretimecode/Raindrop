---
name: build-and-test
description: 构建 Raindrop 项目并运行测试，检查编译和测试状态。当用户需要验证代码变更、运行构建、或执行测试时使用。触发词：构建项目、编译、运行测试、dotnet build、dotnet test、检查编译、跑测试。
---

# 构建与测试

Raindrop 项目的标准构建和测试工作流。

## 前置知识

参考文档：
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览

## 步骤

### 1. 构建项目

```powershell
dotnet build Raindrop.slnx
```

### 2. 运行所有测试

```powershell
dotnet test Raindrop.slnx
```

### 3. 运行特定测试项目

```powershell
dotnet test test/Raindrop.Data.Tests/Raindrop.Data.Tests.csproj
dotnet test test/Raindrop.EventBus.Tests/Raindrop.EventBus.Tests.csproj
dotnet test test/Raindrop.AppConfiguration.Tests/Raindrop.AppConfiguration.Tests.csproj
dotnet test test/Raindrop.AspNetCore.Tests/Raindrop.AspNetCore.Tests.csproj
dotnet test test/Raindrop.CQRS.Tests/Raindrop.CQRS.Tests.csproj
dotnet test test/Raindrop.CQRS.FluentValidation.Tests/Raindrop.CQRS.FluentValidation.Tests.csproj
```

### 4. 运行特定测试类或方法

```powershell
dotnet test test/Raindrop.Data.Tests/Raindrop.Data.Tests.csproj --filter "FullyQualifiedName~OrderRepositoryTests"

dotnet test test/Raindrop.Data.Tests/Raindrop.Data.Tests.csproj --filter "Name~CanCreateOrder"
```

### 5. 详细输出

```powershell
dotnet test Raindrop.slnx --verbosity detailed
```

## 本地基础设施依赖

部分测试依赖本地 PostgreSQL 和 RabbitMQ 实例。运行测试前确保：

```text
PostgreSQL: Host=localhost;Port=5432;Username=postgres;Password=123456;Database=postgres
RabbitMQ:   amqp://admin:admin123@localhost:15672
Redis:      127.0.0.1:6379（无密码，可选）
```

如果本地没有这些服务，部分集成测试会失败。可以先运行不依赖外部服务的单元测试：

```powershell
dotnet test test/Raindrop.CQRS.Tests/Raindrop.CQRS.Tests.csproj
dotnet test test/Raindrop.CQRS.FluentValidation.Tests/Raindrop.CQRS.FluentValidation.Tests.csproj
```

## 测试项目覆盖范围

| 测试项目 | 类型 | 外部依赖 |
|----------|------|----------|
| `Raindrop.CQRS.Tests` | 单元测试 | 无 |
| `Raindrop.CQRS.FluentValidation.Tests` | 单元测试 | 无 |
| `Raindrop.AspNetCore.Tests` | 集成测试 | TestHost |
| `Raindrop.AppConfiguration.Tests` | 单元 + 集成 | PostgreSQL（部分） |
| `Raindrop.Data.Tests` | 集成测试 | PostgreSQL |
| `Raindrop.EventBus.Tests` | 集成测试 | RabbitMQ |

## 检查清单

- [ ] 构建通过无错误无警告
- [ ] 相关测试项目全部通过
- [ ] 新增代码有对应的测试用例覆盖
- [ ] 如果测试依赖 PostgreSQL/RabbitMQ，确认服务已启动
