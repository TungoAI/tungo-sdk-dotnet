# Tungo AI .NET SDK (`Tungo.Sdk`)

Официальный C# / .NET SDK для публичного API платформы **Tungo AI** (.NET 8+).

## Быстрый старт

```csharp
using Tungo.Sdk;

var client = new TungoClient(apiKey: "tg_live_...", companyId: "co_...");
var ans = await client.AskAsync("Покажи дебиторку за октябрь");
Console.WriteLine(TungoMoney.FormatTiyn(TungoMoney.ToTiyn(45000)));
```

## Полный справочник ресурсов и методов

<!-- sdkgen:methods -->
Автоматически сгенерировано из OpenAPI-контракта Tungo Platform API: **13 ресурсов, 82 метода, 22 вебхука**.

| Ресурс | Описание | Методы |
| --- | --- | --- |
| `client.Workspaces` | Организации (workspace), доступные ключу API | `ListAsync` |
| `client.Companies` | Компании организации и их карточки | `ListAsync` · `GetAsync` |
| `client.Platform` | Баланс платформы, книга движений, отчёты о расходе, лимиты, аудит и тарифы моделей | `ListUsageRatesAsync` · `GetAccountAsync` · `GetAuditAsync / GetAuditAllAsync` · `GetLedgerAsync / GetLedgerAllAsync` · `ListLimitsAsync` · `ListRequestsAsync / ListRequestsAllAsync` · `GetUsageAsync` |
| `client.ApiKeys` | Сведения о текущем ключе API и статистика его расхода | `GetCurrentAsync` · `GetCurrentUsageAsync` · `GetUsageAsync` |
| `client.Webhooks` | Исходящие вебхуки организации (Svix): адреса, каталог событий, доставки и проверка подписи | `ListEndpointsAsync / ListEndpointsAllAsync` · `CreateEndpointAsync` · `GetEndpointAsync` · `UpdateEndpointAsync` · `DeleteEndpointAsync` · `ListEndpointAttemptsAsync / ListEndpointAttemptsAllAsync` · `RotateEndpointSecretAsync` · `TestEndpointAsync` · `ListEventTypesAsync` · `ListMessagesAsync / ListMessagesAllAsync` · `ListMessageAttemptsAsync / ListMessageAttemptsAllAsync` · `ResendMessageAsync` · `GetUsageAsync` |
| `client.Chat` | Диалоги с ИИ-бухгалтером: вопросы, потоковый чат (SSE), подсчёт токенов и история | `ListAsync / ListAllAsync` · `CreateAsync / CreateStreamAsync` · `CountTokensAsync` · `GetAsync` · `UpdateAsync` · `DeleteAsync` · `AnswerAsync / AnswerStreamAsync` · `ListEventsAsync` · `StopAsync` |
| `client.Proposals` | Предложения документов ИИ-бухгалтера: просмотр, одобрение записи в 1С и отклонение | `ListAsync / ListAllAsync` · `ApproveBatchAsync` · `GetAsync` · `ApproveAsync` · `RejectAsync` |
| `client.Feed` | Лента событий и предложений компании (страницы и живой поток SSE) | `GetAsync / GetAllAsync` · `ListEventsAsync` |
| `client.Batches` | Пачки обработки банковских выписок, входящих ЭСФ и DirectBank | `ListAsync / ListAllAsync` · `CreateBankStatementAsync` · `CreateEsfPurchasesAsync` · `GetAsync` · `ListEventsAsync` · `StopAsync` · `GetSummaryAsync` · `ListStatementsAsync / ListStatementsAllAsync` · `PollDirectbankAsync` |
| `client.Esf` | Входящие и исходящие электронные счета-фактуры (ИС ЭСФ) | `GetIncomingAsync / GetIncomingAllAsync` · `GetOutgoingAsync / GetOutgoingAllAsync` |
| `client.Forms` | Налоговые формы (ФНО 100.00 и др.): сборка, проверка ИСНА и разрывы данных | `ListAsync` · `BuildAsync` · `ListDataGapsAsync` · `GetIsnaAsync` · `ListNotesAsync` |
| `client.Onec` | Состояние подключения компании к базе 1С | `GetStatusAsync` |
| `client.Automations` | Автоматизации компании: входящие хуки, сценарии, цепочки шагов, запуски и уровень автономности ИИ | `PostHookAsync` · `ListHooksAsync` · `CreateHookAsync` · `GetHookAsync` · `UpdateHookAsync` · `DeleteHookAsync` · `RotateHookAsync` · `TestHookAsync` · `ListRunsAsync / ListRunsAllAsync` · `GetRunAsync` · `CancelRunAsync` · `RetryRunAsync` · `ListScenariosAsync` · `CreateScenarioAsync` · `GetScenarioAsync` · `PutScenarioAsync` · `UpdateScenarioAsync` · `DeleteScenarioAsync` · `RunScenarioAsync` · `GetStateAsync` · `PutStateAsync` · `GetAutonomyAsync` · `PutAutonomyAsync` |
<!-- /sdkgen:methods -->
