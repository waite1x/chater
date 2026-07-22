CREATE TABLE ApiProviders (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    ProviderType INTEGER NOT NULL,
    ApiKey TEXT NOT NULL,
    Endpoint TEXT NULL,
    ModelId TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Skills (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    SystemPrompt TEXT NOT NULL,
    Icon TEXT NULL,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    Version INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Conversations (
    Id TEXT PRIMARY KEY,
    Title TEXT NOT NULL,
    ProviderId TEXT NOT NULL REFERENCES ApiProviders(Id),
    SkillId TEXT NULL REFERENCES Skills(Id),
    ProviderConfiguration TEXT NOT NULL,
    SkillVersion INTEGER NULL,
    AgentType TEXT NOT NULL,
    AgentConfigurationHash TEXT NOT NULL,
    MafVersion TEXT NOT NULL,
    SessionState TEXT NOT NULL,
    SessionStatus INTEGER NOT NULL,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Messages (
    Id TEXT PRIMARY KEY,
    ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
    SequenceNo INTEGER NOT NULL,
    Role INTEGER NOT NULL,
    Content TEXT NOT NULL,
    Status INTEGER NOT NULL,
    ErrorCode TEXT NULL,
    ErrorMessage TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE (ConversationId, SequenceNo)
);

CREATE TABLE AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE INDEX IX_Conversations_UpdatedAt ON Conversations(UpdatedAt DESC);
CREATE INDEX IX_Messages_Conversation_Sequence ON Messages(ConversationId, SequenceNo);
CREATE INDEX IX_Skills_SortOrder ON Skills(SortOrder);

INSERT INTO Skills (Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt) VALUES
('builtin-chat', '通用对话', '通用 AI 助手', '你是 Chater，一个有用的 AI 助手。', '💬', 1, 1, 0, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-translate', '翻译', '自然、忠实的翻译', '你是专业的翻译助手，保持原意并输出自然流畅的译文。', '🌐', 1, 1, 1, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-summarize', '总结', '简洁准确地总结内容', '对输入内容进行简洁、准确的总结。', '📝', 1, 1, 2, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-code-explain', '代码解释', '解释代码逻辑和设计', '解释代码逻辑和关键设计，用通俗语言说明。', '💻', 1, 1, 3, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-polish', '润色', '优化文案表达', '优化文案表达，修正语法错误，保持原意。', '✍️', 1, 1, 4, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
