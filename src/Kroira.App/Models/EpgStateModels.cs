namespace Kroira.App.Models
{
    public enum EpgActiveMode
    {
        None = 0,
        Detected = 1,
        Manual = 2
    }

    public enum EpgStatus
    {
        Unknown = 0,
        Syncing = 1,
        Ready = 2,
        UnavailableNoXmltv = 3,
        FailedFetchOrParse = 4,
        ManualOverride = 5,
        Stale = 6
    }

    public enum EpgSyncResultCode
    {
        None = 0,
        Ready = 1,
        PartialMatch = 2,
        ZeroCoverage = 3,
        NoXmltvAdvertised = 4,
        FetchFailed = 5,
        ParseFailed = 6,
        PersistFailed = 7
    }

    public enum EpgFailureStage
    {
        None = 0,
        Discovery = 1,
        Fetch = 2,
        Parse = 3,
        Match = 4,
        Persist = 5
    }
}
