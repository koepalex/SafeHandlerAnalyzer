using Microsoft.Diagnostics.Runtime;

record GCRootPath(
    ClrRootKind RootKind,
    ulong RootAddress,
    int PathNumber,
    List<GCRootChainLink> Chain,
    bool HasCircularDependency,
    bool MaxDepthReached
);
