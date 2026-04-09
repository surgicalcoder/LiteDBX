namespace LiteDbX.Migrations;

public enum InvalidObjectIdPolicy
{
    Fail,
    SkipDocument,
    LeaveUnchanged,
    RemoveField,
    GenerateNewId
}

