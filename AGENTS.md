# Repository Guidelines

## 使用的本地数据库的连接字符串

Host=localhost;Port=5432;Username=postgres;Password=123456;Database=postgres

## 本地的rabbitMQ
amqp://admin:admin123@localhost:15672
## 本地redis
127.0.0.1:6379 没有用户密码，如果需要redis 的话就用这个。 有的时候数据库不一定好测试使用

## 注意事项
1. 与我的对话， 写文档注释一定要是中文！ 一定要是中文， 这是铁律
2. 写代码，改代码， 删除代码之前一定要先写文档与测试用例.


## 编码风格与命名约定

遵循标准 C# 约定：使用四个空格缩进；公共类型和成员使用 PascalCase；局部变量和参数使用 camelCase；异步方法以 `Async` 结尾。保持 nullable reference types 启用，并处理相关警告。与现有代码一致时，优先使用 file-scoped namespace、implicit usings 和简洁构造函数。 

## Agent skills

### Issue Tracker

Issue 以 markdown 文件形式存放在 `.scratch/<feature-slug>/` 下。详见 `docs/agents/issue-tracker.md`。

### 分类标签

使用默认标签词汇（needs-triage、needs-info、ready-for-agent、ready-for-human、wontfix）。详见 `docs/agents/triage-labels.md`。

### 领域文档

单上下文布局：`CONTEXT.md` + `docs/adr/` 位于仓库根目录。详见 `docs/agents/domain.md`。

