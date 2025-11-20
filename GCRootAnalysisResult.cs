record GCRootAnalysisResult(
    string TypeName,
    ulong ObjectAddress,
    DateTime AnalysisDate,
    List<GCRootPath> RootPaths
);
