---
name: stock-project-requirements-log
description: >-
  Maintains the StockAnalysisSystem user requirements log at docs/USER_REQUIREMENTS_LOG.md.
  Reads that file at the start of substantive work on this repo, appends new asks as backlog
  entries, and updates the log after changes (status, files touched, notes). Use when working
  in StockAnalysisSystem, when the user describes new features or bugs, when continuing prior
  requests, or when the user mentions 需求记录、需求文档、USER_REQUIREMENTS_LOG.
---

# StockAnalysisSystem 用户需求记录

## 必读

在本仓库做**实质性**改动（新功能、修 bug、重构涉及业务）前：

1. 用 Read 打开仓库根目录下的 **`docs/USER_REQUIREMENTS_LOG.md`**，了解当前 backlog、约定与最近结论。
2. 若用户本次提问是**新需求或补充**，在对话中落实后，**必须**更新该文档（见下）。

## 用户提出新问题时

在 `docs/USER_REQUIREMENTS_LOG.md` 的 **「Backlog」** 中追加一条（或合并到已有条目），建议格式：

- **日期**（会话当日）
- **原文摘要**：用户原话或压缩复述（勿过度解读）
- **类型**：功能 / Bug / 优化 / 文档 / 待定
- **状态**：待办 / 进行中 / 已完成 / 搁置
- **涉及模块**（可选）：如 UI、Core、分时、涨停分析

## 每次改完代码后

在同一文档中：

1. 将对应条目的 **状态** 改为「已完成」或「部分完成」，并写**简短实现说明**（改了哪些路径、行为变化）。
2. 若无单独条目（小修），在 **「变更流水」** 追加一行：日期 + 摘要 + 主要文件。
3. 若有**新的全局约定**（如数据源、命名、算法口径），写入 **「约定与口径」**。

## 文档位置（固定）

- 日志正文：`docs/USER_REQUIREMENTS_LOG.md`（与仓库一同版本管理）
- 本 Skill：`.cursor/skills/stock-project-requirements-log/SKILL.md`

若 `docs/` 或文件不存在，先创建再写入。

## 注意

- 日志是**协作备忘**，不替代正式 PRD；避免冗长粘贴整段对话，保留可执行摘要即可。
- 敏感信息（密钥、账号）不得写入。
