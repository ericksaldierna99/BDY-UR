namespace UrHmi.Tools.Screwdriver;

internal static class SpinBridgeNodes
{
    public const string NamespaceUri = "http://spin-robotics.com/TighteningSystem/";
    public const string IjtBaseNamespaceUri = "http://opcfoundation.org/UA/IJT/Base/";
    public const string IjtTighteningNamespaceUri = "http://opcfoundation.org/UA/IJT/Tightening/";
    public const string TighteningSystem = "ns=1;i=5003";
    public const string ResultManagement = "ns=1;i=5011";
    public const string TighteningSystemAbsolute = "nsu=" + NamespaceUri + ";i=5003";
    public const string ResultManagementAbsolute = "nsu=" + NamespaceUri + ";i=5011";
    public const string TighteningSystemIjt = "nsu=" + IjtTighteningNamespaceUri + ";i=5003";
    public const string ResultManagementIjt = "nsu=" + IjtTighteningNamespaceUri + ";i=5011";
    public const string ObjectsFolder = "i=85";

    public const string TighteningSystemName = "TighteningSystem";
    public const string ResultManagementName = "ResultManagement";
    public const string ResultsName = "Results";
    public const string LatestResultName = "LatestResult";
    public const string OverallResultValuesName = "overallResultValues";
}
