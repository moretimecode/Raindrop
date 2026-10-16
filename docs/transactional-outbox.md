# 本地消息表与 Outbox 设计实现

## 职责与实现范围

`Raindrop.Data.EFCore` 提供生产端 Outbox 同事务登记：业务数据和发送意图使用同一个 DbContext、连接和事务提交，聚合集成事件与直接 `IEventBus.PublishAsync` 共用该能力。

**当前只保存 Pending 消息，不会实际发送 MQ。** 后台认领、派发、发布确认、状态更新、扫描重试与清理尚未实现，因此还不能删除业务侧的可靠交付恢复措施。消费 Inbox、事务消费成功记录及结果通知也不由本组件提供。

未启用 Outbox 时保留默认行为：直接 Publish 即时调用 transport，聚合集成事件在 UoW 确认提交后尽力发送。启用后要求活动 Raindrop UoW，不能回退本地调用或即时 MQ 发送。

## 组件分工

| 组件 | 职责 |
|---|---|
| `RoutingEventBus` | 将集成 Publish 交给 `IEventOutbox`，普通领域事件仍本地处理 |
| `DomainEventProcessor` | 本地事件处理至稳定后，将聚合集成事件交给同一登记器 |
| `EntityFrameworkCoreEventOutbox<TDbContext>` | 校验、生成固定消息快照，并把全部目标记录加入当前 DbContext |
| `OutboxContractCatalog` | 冻结运行时事件类型到稳定契约名/版本的映射 |
| `IEventTransport.CaptureTarget` | 纯粹冻结发送地址，不创建连接、不发送 |
| `UnitOfWorkSession<TDbContext>` | 保存当前阶段、原提交尝试身份与已分配消息身份 |
| `OutboxTransactionBinding` | 在 scope 内确定唯一活动 UoW 及实际 DbContext 归属 |
| `OutboxMessage` / `AddRaindropOutbox()` | 持久实体及 EF 模型映射 |

业务不需要新建 Outbox 事件类型、编写存储接口、包装 transport 或手工操作消息表。UoW 只负责提交顺序，登记器不独立保存和提交数据库。

## 宿主装配

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.EFCore;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

services.AddRaindropData<AppDbContext>(options => options.UseSqlServer(connectionString));
services.AddMediatRLocalEventBus(typeof(Program).Assembly);
services.AddRabbitMqEventBus(typeof(Program).Assembly);
services.AddRaindropOutbox<AppDbContext>(options =>
    options.AddContract<OrderPaidIntegrationEvent>("order.paid", "1"));

[RabbitMqEvent("orders", "order.paid")]
public sealed record OrderPaidIntegrationEvent(Guid OrderId) : IIntegrationEvent;
```

本地 Resolver 只在需要处理进程内领域事件时使用。RabbitMQ 连接和消费配置见 [RabbitMQ 组件](rabbitmq-consumer-hostedservice.md)。不要求发布进程存在该集成事件的 handler，也不根据 handler 自动补发送路由。

业务 DbContext 必须把消息实体纳入模型：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // 其他业务实体映射。
    modelBuilder.AddRaindropOutbox();
}
```

表随业务自己的 EF Migration 创建和升级；框架不自动创建数据库/schema，不另开连接补写消息。仅配置 AddRaindropOutbox 而未把模型/表部署到数据库，不能提供登记能力。

契约名最长 200 字符、版本最长 64 字符，均不能为空。一个事件类型只映射一个契约，同一名称/版本不能映射多个类型；运行时按实际事件类型查找，不隐式继承父类型注册。装配结束后配置冻结；相同宿主不能重复或覆盖登记器，也不支持多个消息 DbContext。

首批目标为 SQL Server/MySQL，持久映射使用 EF 通用能力，provider 由宿主配置。当前 SQLite/PostgreSQL 验证不代替 SQL Server/MySQL 的驱动、迁移与物理故障验证，尚未完成这两个目标组合的认证。

## 业务发布

业务仍在正常 UoW 中修改状态并产生事件：

```csharp
// 聚合方法内部。
AddDomainEvent(new OrderPaidIntegrationEvent(Id));
```

也可以在应用用例中直接发布：

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);
await orderRepository.UpdateAsync(order);
await eventBus.PublishAsync(new OrderPaidIntegrationEvent(order.Id), cancellationToken);
await uow.CommitAsync(cancellationToken);
```

两种入口提供一致的原子提交语义。直接 Publish 在调用时冻结，聚合事件在领域事件准备结束时冻结；事件对象后续改变不会重写已登记内容。同一意图在两处各发布一次会产生两条逻辑消息，框架不按对象引用去重。

Publish 返回只表示当前事务接受了意图，不表示事务提交、broker 接收或消费成功。消息允许重复，消息身份去重也不替代业务执行幂等。

## 事务归属与登记顺序

```text
活动 Raindrop UoW 与实际 DbContext 身份检查
  → 验证原关系型事务及消息模型
  → 查稳定契约
  → 解析并预检全部发送目标
  → 序列化实际运行时类型，固定 UTF-8 JSON 与 hash
  → 分配逻辑消息及各目标投递身份
  → 全部记录加入原 DbContext
  → UoW 关闭登记窗口
  → SaveChanges + 原事务 Commit
```

启用后，同一 DI scope 只允许一个活动 UoW；重复 Begin 同一个实例加入原事务，不能同时开启另一个上下文的 UoW。登记比较实际 DbContext 对象身份和原事务 ID，不按类型或连接字符串推测共享事务。

其他 DbContext 的显式类型化 UoW 可顺序执行不含集成发布的事务。从这些事务发布集成事件会失败，并使真正活动的 UoW 不可继续提交；不能误伤未使用的消息库会话后仍把来源业务提交。失败/Unknown 回滚后仍保留终态，需要丢弃 scope；正常提交完成或正常回滚后才允许后续 UoW 接管。释放其他未使用的 UoW 不会解除原所有者的绑定。

无活动 UoW 的发布会被拒绝，但不会妨碍随后正常 Begin；自行开启 EF 事务不能冒充 Raindrop UoW。Outbox 工作单元必须经 AddRaindropData 共享会话装配，手工构造或用旧准备器覆盖而丢失 Outbox 依赖时拒绝 Begin。

所有目标预检完成后才加入 ChangeTracker。以下错误均在业务提交前失败：

- 无路由、默认 Local 回退或 Local/远程混合。
- 缺少稳定契约、无适配器、同一路由匹配多个适配器。
- transport 不支持快照、目标格式无效或同一发布含重复目标。
- 序列化失败、取消、非关系型 provider、缺少消息模型或原事务不匹配。

活动事务中的登记失败会阻止提交，即使业务捕获了异常。最终保存开始后关闭登记窗口，保存拦截器/回调中的迟到 Publish 同样不能让业务漏存意图后继续提交。

## 本地消息表

实体 `OutboxMessage` 位于 `Raindrop.Data.EFCore`，默认表为 **`raindrop_outbox`**，继承业务模型的默认 schema/数据库。属性对外只读，`Payload` getter 返回副本；EF 使用 `_payload` 字段读写实际 bytes。

下表描述实际 CLR 类型、EF 长度及约束。物理 SQL 类型由 provider 生成，没有单独维护一份跨数据库 DDL；所有字段均非空。字符串的“非 Unicode”指当前 EF 映射配置，目标 provider 的具体类型仍需迁移验证。

| 字段 | 类型/长度 | 语义与生成方式 |
|---|---|---|
| `DeliveryId` | string(64)，非 Unicode，主键 | 每目标独立投递身份；当前使用 Guid 的 N 格式 |
| `MessageId` | string(64)，非 Unicode | 一次发布的逻辑身份，多目标共用；当前使用 Guid 的 N 格式 |
| `CommitAttemptId` | Guid | 原 UoW Begin 时分配的提交尝试身份 |
| `ContractName` | string(200) | 显式注册的稳定契约名 |
| `ContractVersion` | string(64) | 显式注册的契约版本 |
| `LegacyTypeName` | string(2000) | 运行时事件的 AssemblyQualifiedName，仅供兼容类型头，不是稳定契约 |
| `Payload` | byte[] | 已固定的 UTF-8 JSON，按实际运行时类型序列化 |
| `PayloadHash` | string(64)，非 Unicode | bytes 的 SHA-256 大写十六进制值 |
| `Transport` | string(100)，非 Unicode | 稳定 transport 协议名，例如 rabbitmq |
| `TargetVersion` | string(32)，非 Unicode | 发送地址格式版本 |
| `TargetJson` | string，不限制最大长度 | 固定目标对象 JSON |
| `CreatedAtUtc` | DateTime | 登记器使用 DateTime.UtcNow 生成，同一次发布各目标共用 |
| `Status` | string(32)，非 Unicode | CLR 实体初始化为 Pending；不是数据库默认值或已交付证明 |

当前索引：

| 索引 | 类型 | 用途 |
|---|---|---|
| `DeliveryId` | 主键 | 唯一定位一条目标投递记录 |
| `MessageId` | 非唯一 | 查询同一逻辑消息的所有目标 |
| `CommitAttemptId` | 非唯一 | 核实原事务中的发送意图 |
| `(Status, CreatedAtUtc)` | 非唯一 | 按状态及登记时间读取；目前尚无扫描器消费该索引 |

`MessageId` 不是唯一键，因为一次发布可以对应多条目标投递。多个目标共用 payload/hash 和 MessageId，各自具有 DeliveryId 和地址；当前表没有租约、重试次数、下次执行时间或已确认时间字段，也没有后台状态迁移逻辑。

RabbitMQ 当前地址示例：

```json
{"exchange":"orders","routingKey":"order.paid"}
```

对应 `Transport = rabbitmq`、`TargetVersion = 1`。消费 queue 不进入该快照。Adapter 负责为同一目标生成一致的规范化 JSON，框架按 transport、版本和 JSON 的值检测重复；登记不会自动创建 exchange/queue。

## 提交结果与恢复边界

| 情况 | 当前行为 |
|---|---|
| 登记/保存失败或明确回滚 | 业务与 Outbox 在原事务中一起撤销，不即时发送 |
| 确认 Commit 成功 | 业务和 Pending 记录共同持久化，执行尽力后置回调 |
| Commit 调用结果不明 | 抛 Unknown，保留原 CommitAttemptId、MessageIds，不分配新身份重做业务 |
| 提交后回调/诊断失败 | 不回滚已提交业务，也不改变消息已提交事实 |
| 进程在提交后退出 | Pending 仍在数据库中，但目前没有派发/扫描器自动处理 |

原记录存在可作为原事务提交的核实依据；暂时查空不能证明未提交。框架当前不提供权威核实流程、请求幂等或端到端 exactly-once。

生产发送、消费业务重试、结果通知是不同职责。后续若增加派发与恢复，应从已保存的身份、bytes 和目标执行，不能重新序列化业务对象或调用公开 Publish 再次登记；这些执行与状态迁移尚未实现。

## 验证覆盖

`test/Raindrop.Data.Tests` 包含两入口登记、内容冻结、多目标身份、路由/配置失败、物理保存回滚、迟到发布、重入、跨上下文归属及 Unknown 终态测试；PostgreSQL 测试使用独立连接核实业务与消息共同提交/回滚。

`test/Raindrop.EventBus.Tests` 验证 RabbitMQ 目标快照不打开连接、旧 transport 没有快照时明确拒绝登记。完整解决方案命令及隔离连接配置见[工程规范](engineering-guidelines.md#11-测试约定)。
