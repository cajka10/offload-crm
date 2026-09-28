namespace Offload.Crm.Domain.Enums;

/// <summary>D-04.</summary>
public enum CustomerStatus
{
    Lead,
    Active,
    Inactive,
}

/// <summary>D-05. Shared by projects and tasks.</summary>
public enum Priority
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>D-02: fixed task statuses.</summary>
public enum TaskItemStatus
{
    Todo,
    InProgress,
    Blocked,
    Done,
}

public enum ActivityType
{
    Call,
    Email,
    Meeting,
    Note,
    Other,
}
