public abstract class BlobNodeExtraction
{
    public static void ProcessBlobsForSpecifiedTree(string treeHash, CommitNodeExtraction CommitNode)
    {
        // Get the details of the Blobs in this Tree
        string tree = FileType.GetContents(CommitNode.CommitTreeDetails.Groups[1].Value, GlobalVars.workingArea);

        var blobsInTree = Regex.Matches(tree, @"blob ([0-9a-f]{4})[0-9a-f]{36}.([\w\.]+)");

        foreach (Match blobMatch in blobsInTree)
        {
            string blobHash = blobMatch.Groups[1].Value;
            string blobContents = string.Empty;

            FileType.GetContents(blobHash, GlobalVars.workingArea);

            //Console.WriteLine($"\t\t-> blob {blobHash} {blobMatch.Groups[2]}");
            if (GlobalVars.EmitNeo && !FileType.DoesNeo4jNodeExistAlready(Neo4jHelper.session, blobHash, "blob"))
            {
                Neo4jHelper.AddBlobToNeo(Neo4jHelper.session, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents);
            }

            GitBlobs.Add(treeHash, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents);

            if (GlobalVars.EmitNeo && !Neo4jHelper.DoesTreeToBlobLinkExist(Neo4jHelper.session, CommitNode.CommitTreeDetails.Groups[1].Value, blobHash))
            {
                if (GlobalVars.EmitNeo)
                    Neo4jHelper.CreateLinkNeo(Neo4jHelper.session, CommitNode.CommitTreeDetails.Groups[1].Value, blobMatch.Groups[1].Value, "", "");
            }

            GitTrees.CreateTreeToBlobLink(CommitNode.CommitTreeDetails.Groups[1].Value, blobMatch.Groups[1].Value);
        }
        // Any SubTrees in this Tree?
        var TreesInTree = Regex.Matches(tree, @"tree ([0-9a-f]{4})[0-9a-f]{36}.([\w\.]+)");
        foreach (Match treeMatch in TreesInTree)
        {
            string subtreeHash = treeMatch.Groups[1].Value;
            string subTreeFolderName = treeMatch.Groups[2].Value;
            GitTrees.Add(subtreeHash, treeHash, subTreeFolderName);
            BlobNodeExtraction.ProcessSubTeeesForSpecifiedTree(subtreeHash);
        }
    }


    public static void ProcessSubTeeesForSpecifiedTree(string treeHash)
    {
        // Get the details of the Blobs in this Tree
        string tree = FileType.GetContents(treeHash, GlobalVars.workingArea);

        var blobsInTree = Regex.Matches(tree, @"blob ([0-9a-f]{4})[0-9a-f]{36}.([\w\.]+)");

        foreach (Match blobMatch in blobsInTree)
        {
            string blobHash = blobMatch.Groups[1].Value;
            string blobContents = string.Empty;

            FileType.GetContents(blobHash, GlobalVars.workingArea);

            GitBlobs.Add(treeHash, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents);

            GitTrees.CreateTreeToBlobLink(treeHash, blobMatch.Groups[1].Value);
        }
    }
}