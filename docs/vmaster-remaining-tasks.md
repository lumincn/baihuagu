# 百花 (baihuagu) — 剩余任务

> 更新日期：2026-07-31
> 前置文档：project-manager/docs/vmaster-tasks.md

---

## 进行中

| 任务 | 状态 | 说明 |
|------|------|------|
| T-SDK 百花授权全链路 | ✅ 完成 | Kotlin/C#/ArkTS 三端对齐，纯 WebSocket 授权 |
| T-SDK SDK 架构重组 | ✅ 完成 | 6 SDK SRP 拆分，vault-sdk 接口注入解耦 |
| T-SDK ArkTS AuthService | ✅ 完成 | AuthServiceImpl + AuthViewModel + EntryAbility 迁移 |
| T-SDK C# 确认 | ✅ 完成 | SRP 审计 + RequestSignatureService 冗余清理 |
| T-Kotlin MainViewModel 清理 | ✅ 完成 | 移除 PushSyncInfo/sdkPushService 死代码 |

---

## 已完成

| 任务 | 状态 | 关键文件 |
|------|------|---------|
| T-B01 百花后端 | ✅ 完成 | `TaskRunner.Family/Controllers/AI/MasterController.cs` + `Services/MasterPromptBuilder.cs` + `TaskRunner.Data/FamilyDbContext.cs`(7张表) + `TaskRunner.Contracts/Master/MasterDtos.cs` |
| T-B02 百花WebUI对话页 | ✅ 完成 | `WebUI.Family/Pages/MasterChat.razor` |
| T-B03 百花WebUI阶段页 | ✅ 完成 | `WebUI.Family/Pages/MasterStage.razor` |
| T-B04 百花导航集成 | ✅ 完成 | `WebUI.Family/Shared/FamilyNavMenu.razor` |
| T-014-B 百花端数据淘汰 | ✅ 完成 | `MasterController.cs`(compress:481/evict:547) + `Services/MasterDataRetentionService.cs`(后台任务) |
| T-015-B 百花端免责声明 | ✅ 完成 | `Pages/MasterDisclaimerDialog.razor` + `MasterChat.razor`(创建前弹窗) |
| T-016-B 百花端考试大纲数据 | ✅ 完成 | `TaskRunner.Family/Data/ExamOutlines/`(教资/会计/软考/执业医师.json) + `MasterPromptBuilder.LoadAllOutlines()` |
| T-019-B 百花端知识库聚焦API | ✅ 完成 | `Controllers/AI/VaultFocusController.cs` + `FamilyDbContext.cs`(VaultFocusStates+VaultFreeStates表) + `MasterController.cs`(阶段完成自动切换) |
