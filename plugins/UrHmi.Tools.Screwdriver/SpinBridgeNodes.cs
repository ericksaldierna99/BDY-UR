namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Address space from Spin's OPC UA Server guide
/// (https://docs.spin-robotics.com/stable/opc-ua/) and the IJT / Machinery-Result
/// companion types that guide points at.
/// </summary>
internal static class SpinBridgeNodes
{
    public const string NamespaceUri = "http://spin-robotics.com/TighteningSystem/";
    public const string MachineryResultNamespaceUri = "http://opcfoundation.org/UA/Machinery/Result/";
    public const string IjtBaseNamespaceUri = "http://opcfoundation.org/UA/IJT/Base/";

    /// <summary>Objects / TighteningSystem in the Spin guide (ns=1;i=5003).</summary>
    public const string TighteningSystem = "ns=1;i=5003";

    /// <summary>ResultManagement event source and Results parent (ns=1;i=5011).</summary>
    public const string ResultManagement = "ns=1;i=5011";

    public const string TighteningSystemAbsolute = "nsu=" + NamespaceUri + ";i=5003";
    public const string ResultManagementAbsolute = "nsu=" + NamespaceUri + ";i=5011";

    /// <summary>
    /// ResultReadyEventType in OPC 40001-101. Spin UAExpert: SimpleEvents &gt;
    /// ResultReadyEventType. Numeric id is from the Machinery/Result NodeSet.
    /// </summary>
    public const string ResultReadyEventTypeAbsolute = "nsu=" + MachineryResultNamespaceUri + ";i=1001";

    public const string TighteningSystemName = "TighteningSystem";
    public const string ResultManagementName = "ResultManagement";
    public const string ResultsName = "Results";
    public const string LatestResultName = "LatestResult";
    public const string ResultReadyEventTypeName = "ResultReadyEventType";
    public const string JoiningSystemResultReadyEventTypeName = "JoiningSystemResultReadyEventType";
    public const string ResultFieldName = "Result";
    public const string ResultContentName = "ResultContent";
    public const string OverallResultValuesName = "OverallResultValues";
    public const string FinalTorqueName = "Final Torque";
    public const string FinalAngleName = "Final Angle";

    public static readonly string[] ResultManagementIds =
    [
        ResultManagementAbsolute,
        ResultManagement
    ];

    public static readonly string[] TighteningSystemIds =
    [
        TighteningSystemAbsolute,
        TighteningSystem
    ];

    public static readonly string[] PathFromTighteningSystem =
    [
        ResultManagementName,
        ResultsName,
        LatestResultName
    ];

    public static readonly string[] PathFromResultManagement =
    [
        ResultsName,
        LatestResultName
    ];
}
