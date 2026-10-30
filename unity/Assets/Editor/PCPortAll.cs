// PC port: batch-mode entry points.
//   CLI: -executeMethod PCPortAll.UnbatchAndBuild   (first run on a fresh export; un-batching needs the original YAML)
public static class PCPortAll
{
	public static void UnbatchAndBuild()
	{
		PCPortUnbatch.RunAll();
		PCPortBuild.BuildWindows64();
	}
}
