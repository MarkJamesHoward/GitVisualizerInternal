﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using Neo4j.Driver;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Timers;
bool EmitJsonOnly = true;

System.Timers.Timer processTimer = null;

//string testPath = @"C:\dev\rep1\";
string testPath = "";
string CommitNodesJsonFile = @"C:\github\GitGraph\Json\CommitGitInJson.json";
string TreeNodesJsonFile = @"C:\github\GitGraph\Json\TreeGitInJson.json";
string BlobNodesJsonFile = @"C:\github\GitGraph\Json\BlobGitInJson.json";
string HeadNodesJsonFile = @"C:\github\GitGraph\Json\HeadGitInJson.json";
string BranchNodesJsonFile = @"C:\github\GitGraph\Json\BranchGitInJson.json";


string head = Path.Combine(testPath, @".git\");
string path = Path.Combine(testPath, @".git\objects\");
string branchPath = Path.Combine(testPath, @".git\refs\heads");
string remoteBranchPath = Path.Combine(testPath, @".git\refs\remotes");
bool debug = false;

if (args?.Length > 0 || debug)
{
    if (!debug)
    {
        //Console.WriteLine(args?[0] ?? "No Args");

        if (args?[0] == "--bare")
        {
            head = Path.Combine(testPath, @".\");
            path = Path.Combine(testPath, @".\objects\");
            branchPath = Path.Combine(testPath, @".\refs\heads");
            remoteBranchPath = Path.Combine(testPath, @".\refs\remotes");
        }

        if (args?[0] == "--json")
        {
            EmitJsonOnly = true;
            //Console.WriteLine("Emitting Json only");
        }
    }
}

string MyExePath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;
string MyExeFolder = System.IO.Path.GetDirectoryName(MyExePath);
MyExeFolder = MyExeFolder.Replace(@"file:\", "");

var builder = new ConfigurationBuilder()
                               .SetBasePath(MyExeFolder)
                               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

string password = builder.Build().GetSection("docker").GetSection("password").Value;
string uri = builder.Build().GetSection("docker").GetSection("url").Value;
string username = builder.Build().GetSection("docker").GetSection("username").Value;

// string password = builder.Build().GetSection("cloud").GetSection("password").Value;
// string uri = builder.Build().GetSection("cloud").GetSection("url").Value;
// string username = builder.Build().GetSection("cloud").GetSection("username").Value;

List<string> HashCodeFilenames = new List<string>();

// Make one run to start with before waiting for files to change
await main();

//Console.WriteLine($"Watching for changes... ");

using var watcher = new FileSystemWatcher("./");
{
    watcher.NotifyFilter = NotifyFilters.Attributes
                            | NotifyFilters.CreationTime
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.FileName
                            | NotifyFilters.LastAccess
                            | NotifyFilters.LastWrite
                            | NotifyFilters.Security
                            | NotifyFilters.Size;

    //watcher.Changed += OnChanged;
    watcher.Created += OnChanged;
    //watcher.Deleted += OnChanged;
    //watcher.Renamed += OnChanged;
    //watcher.Error += OnError;

    watcher.Filter = "*.*";
    watcher.IncludeSubdirectories = true;
    watcher.EnableRaisingEvents = true;

    //Console.WriteLine("Press enter to exit.");
    Console.ReadLine();
}

async void OnChanged(object sender, FileSystemEventArgs e)
{
    if (e.Name.Contains(".lock", StringComparison.CurrentCultureIgnoreCase) ||
     e.Name.Contains("tmp", StringComparison.CurrentCultureIgnoreCase))
    {
        Console.WriteLine("Ignoring " + e.Name + e.ChangeType.ToString());
        return;
    }
    
    if (processTimer == null)
    {
        // First file, start timer.
        await main();
        Console.WriteLine($"Changed: {e.FullPath}");
        processTimer = new System.Timers.Timer(2000);
        processTimer.Elapsed += OnEventExecution;
        processTimer.Start();
    }
    else {
        Console.WriteLine("Considered part of batch");
    }
    
}

void OnEventExecution(Object source, ElapsedEventArgs e)
{
    processTimer.Stop();
    processTimer = null;
}

async Task<bool> main()
{
    List<CommitNode> CommitNodes = new List<CommitNode>();
    List<TreeNode> TreeNodes = new List<TreeNode>();
    List<Blob> blobs = new List<Blob>();
    List<Branch> branches = new List<Branch>();

    HEAD HEAD = new HEAD();


    // Get all the files in the .git/objects folder
    try
    {
        List<string> remoteBranchFiles = new List<string>();

        List<string> branchFiles = Directory.GetFiles(branchPath).ToList();
        if (Directory.Exists(remoteBranchPath))
        {
            List<string> RemoteDirs = Directory.GetDirectories(remoteBranchPath).ToList();
            foreach (string remoteDir in RemoteDirs)
            {
                foreach (string file in Directory.GetFiles(remoteDir).ToList())
                {
                    var DirName = new DirectoryInfo(Path.GetDirectoryName(remoteDir + "\\"));
                    remoteBranchFiles.Add(file);
                }
            }
        }
        List<string> directories = Directory.GetDirectories(path).ToList();
        List<string> files = new List<string>();

        IDriver _driver;
        ISession session = null;

        if (!EmitJsonOnly)
        {
            _driver = GetDriver(uri, username, password);
            session = _driver.Session();
        }

        if(!EmitJsonOnly)
            ClearExistingNodesInNeo(session);

        foreach (string dir in directories)
        {
            files = Directory.GetFiles(dir).ToList();

            foreach (string file in files)
            {
                string hashCode = Path.GetFileName(dir) + Path.GetFileName(file).Substring(0, 2);

                HashCodeFilenames.Add(hashCode);

                string fileType = FileType.GetFileType(hashCode);

                if (fileType.Contains("commit"))
                {
                    ////Console.WriteLine($"{fileType.TrimEnd('\n', '\r')} {hashCode}");

                    string commitContents = FileType.GetContents(hashCode);
                    var match = Regex.Match(commitContents, "tree ([0-9a-f]{4})");
                    var commitParent = Regex.Match(commitContents, "parent ([0-9a-f]{4})");
                    var commitComment = Regex.Match(commitContents, "\n\n(.+)\n");

                    if (match.Success)
                    {
                        // Get details of the tree,parent and comment in this commit
                        string treeHash = match.Groups[1].Value;
                        //Console.WriteLine($"\t-> tree {treeHash}");

                        string parentHash = commitParent.Groups[1].Value;
                        //Console.WriteLine($"\t-> parent commit {commitParent}");

                        string comment = commitComment.Groups[1].Value;
                        comment = comment.Trim();

                        if (!EmitJsonOnly)
                        {
                            AddCommitToNeo(session, comment, hashCode, commitContents);
                        }

                        if (!EmitJsonOnly && !FileType.DoesNodeExistAlready(session, treeHash, "tree"))
                        {
                            if (!EmitJsonOnly)
                                AddTreeToNeo(session, treeHash, FileType.GetContents(treeHash));
                        }

                        CreateTreeJson(treeHash, FileType.GetContents(treeHash), TreeNodes);
                        CreateCommitJson(parentHash, comment, hashCode, treeHash, commitContents, CommitNodes);

                        if (!EmitJsonOnly)
                        {
                            CreateCommitLinkNeo(session, hashCode, treeHash, "", "");
                        }

                        // Get the details of the Blobs in this Tree
                        string tree = FileType.GetContents(match.Groups[1].Value);
                        var blobsInTree = Regex.Matches(tree, @"blob ([0-9a-f]{4})[0-9a-f]{36}.([\w\.]+)");

                        foreach (Match blobMatch in blobsInTree)
                        {
                            string blobHash = blobMatch.Groups[1].Value;
                            string blobContents = FileType.GetContents(blobHash);

                            //Console.WriteLine($"\t\t-> blob {blobHash} {blobMatch.Groups[2]}");
                            if (!EmitJsonOnly && !FileType.DoesNodeExistAlready(session, blobHash, "blob"))
                            {
                                if (!EmitJsonOnly)
                                    BlobCode.AddBlobToNeo(session, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents);
                            }
                            BlobCode.AddBlobToJson(treeHash, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents, blobs);

                            if (!EmitJsonOnly && !DoesTreeToBlobLinkExist(session, match.Groups[1].Value, blobHash))
                            {
                                if (!EmitJsonOnly)
                                    CreateLinkNeo(session, match.Groups[1].Value, blobMatch.Groups[1].Value, "", "");
                            }

                            CreateTreeToBlobLinkJson(match.Groups[1].Value, blobMatch.Groups[1].Value, TreeNodes);
                        }
                    }
                    else
                    {
                        //Console.WriteLine("No Tree found in Commit");
                    }
                }
            }

        }
        // Add the Branches
        foreach (var file in branchFiles)
        {
            var branchHash = await File.ReadAllTextAsync(file);
            if (!EmitJsonOnly)
            {
                AddBranchToNeo(session, Path.GetFileName(file), branchHash);
                CreateBranchLinkNeo(session, Path.GetFileName(file), branchHash.Substring(0, 4));
            }
            AddBranchToJson(Path.GetFileName(file), branchHash.Substring(0, 4), branches);
        }

        // Add the Remote Branches
        foreach (var file in remoteBranchFiles)
        {
            var branchHash = await File.ReadAllTextAsync(file);
            if (!EmitJsonOnly)
            {
                AddRemoteBranchToNeo(session, Path.GetFileName(file), branchHash);
                CreateRemoteBranchLinkNeo(session, $"remote{Path.GetFileName(file)}", branchHash.Substring(0, 4));
            }
        }
        if (!EmitJsonOnly)
        {
            AddCommitParentLinks(session, path);
            BlobCode.AddOrphanBlobs(session, branchPath, path, blobs);
            GetHEAD(session, head);
        }

        if (EmitJsonOnly){
            BlobCode.AddOrphanBlobsToJson(branchPath, path, blobs);
        }

        if (EmitJsonOnly)
        {
            OutputNodesJson(CommitNodes, TreeNodes, blobs, CommitNodesJsonFile);
            OutputNodesJson(TreeNodes, TreeNodes, blobs, TreeNodesJsonFile);
            OutputNodesJson(blobs, TreeNodes, blobs, BlobNodesJsonFile);
            OutputHEADJson(HEAD, HeadNodesJsonFile, head);
            OutputBranchJson(branches, TreeNodes, blobs, BranchNodesJsonFile);
        }
    }
    catch (Exception e)
    {
        //Console.WriteLine($"Error while getting files in {path} {e.Message}");
    }

    static void CreateCommitJson(string parentCommitHash, string comment, string hash, string treeHash, string contents, List<CommitNode> CommitNodes)
     {
        CommitNode n = new CommitNode();
        n.text = comment;
        n.hash = hash;
        n.parent = parentCommitHash;
        n.tree = treeHash;

        if (!CommitNodes.Exists(i => i.hash == n.hash))
            CommitNodes.Add(n);
    }

    static void CreateTreeJson(string treeHash, string contents, List<TreeNode> TreeNodes) 
    {
        TreeNode tn = new TreeNode();
        tn.hash = treeHash;
        tn.blobs = new List<string>();

        if (!TreeNodes.Exists(i => i.hash == tn.hash))
        {
            TreeNodes.Add(tn);
        }
    }

    static void OutputHEADJson(HEAD head, string JsonPath, string path) 
    {
        string HeadContents = File.ReadAllText(Path.Combine(path, "HEAD"));
        //Console.WriteLine("Outputting JSON HEAD");
        string HEADHash = "";

        // Is the HEAD detached in which case it contains a Commit Hash
        Match match = Regex.Match(HeadContents, "[0-9a-f]{40}");
        if (match.Success)
        {
            //Console.WriteLine("Outputting JSON HEAD match found 1");
            HEADHash = match.Value.Substring(0, 4);
        }
        match = Regex.Match(HeadContents, @"ref: refs/heads/(\w+)");
        if (match.Success)
        {
            //Console.WriteLine("Outputting JSON HEAD match found 2");

            //Console.WriteLine("HEAD Branch extract: " + match.Groups[1]?.Value);
            HEADHash = match.Groups[1].Value;
            //CreateHEADTOBranchLinkNeo(session, branch);
        }
        HEAD h = new HEAD();
        h.hash = HEADHash;

        var Json = string.Empty;
        Json = JsonSerializer.Serialize(h);

        //Console.WriteLine(Json);
        File.WriteAllText(JsonPath, Json);
    }

    
    static void OutputBranchJson<T>(List<T> Nodes, List<TreeNode> TreeNodes, List<Blob> blobs, string JsonPath) 
    {
        var Json = string.Empty;

        Json = JsonSerializer.Serialize(Nodes);

        //Console.WriteLine(Json);
        File.WriteAllText(JsonPath, Json);
    }

    static void OutputNodesJson<T>(List<T> Nodes, List<TreeNode> TreeNodes, List<Blob> blobs, string JsonPath) 
    {
        var Json = string.Empty;

        Json = JsonSerializer.Serialize(Nodes);

        //Console.WriteLine(Json);
        File.WriteAllText(JsonPath, Json);
    }
   

    static void GetHEAD(ISession session, string path)
    {
        string HeadContents = File.ReadAllText(Path.Combine(path, "HEAD"));

        // Is the HEAD detached in which case it contains a Commit Hash
        Match match = Regex.Match(HeadContents, "[0-9a-f]{40}");
        if (match.Success)
        {
            string HEADHash = match.Value.Substring(0, 4);
            //Create the HEAD Node
            AddHeadToNeo(session, HEADHash, HeadContents);
            //Create Link to Commit
            CreateHEADTOCommitLinkNeo(session, HEADHash);
        }

        match = Regex.Match(HeadContents, @"ref: refs/heads/(\w+)");
        if (match.Success)
        {
            //Console.WriteLine("HEAD Branch extract: " + match.Groups[1]?.Value);
            string branch = match.Groups[1].Value;
            //Create the HEAD Node
            AddHeadToNeo(session, branch, HeadContents);
            //Create Link to Commit
            CreateHEADTOBranchLinkNeo(session, branch);
        }
    }



    static bool DoesTreeToBlobLinkExist(ISession session, string treeHash, string blobHash)
    {
        string query = "MATCH (t:tree { hash: $treeHash })-[r:blob]->(b:blob {hash: $blobHash }) RETURN r, b";
        var result = session.Run(
                query,
                new { treeHash, blobHash });

        foreach (var record in result)
        {
            return true;
        }

        return false;
    }

 


    static void AddCommitParentLinks(ISession session, string path)
    {
        List<string> directories = Directory.GetDirectories(path).ToList();

        foreach (string dir in directories)
        {
            var files = Directory.GetFiles(dir).ToList();

            foreach (string file in files)
            {

                string hashCode = Path.GetFileName(dir) + Path.GetFileName(file).Substring(0, 2);
                string fileType = FileType.GetFileType(hashCode);

                if (fileType.Contains("commit"))
                {
                    string commitContents = FileType.GetContents(hashCode);
                    var commitParent = Regex.Match(commitContents, "parent ([0-9a-f]{4})");

                    if (commitParent.Success)
                    {
                        string parentHash = commitParent.Groups[1].Value;
                        //Console.WriteLine($"\t-> parent commit {commitParent}");

                        CreateCommitTOCommitLinkNeo(session, hashCode, parentHash);
                    }
                }
            }
        }
    }

    static IDriver GetDriver(string uri, string username, string password)
    {
        IDriver _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        return _driver;
    }

    static void ClearExistingNodesInNeo(ISession session)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (n) DETACH DELETE n",
                new { });

            return result;
        });
    }

    static void CreateTreeToBlobLinkJson(string parent, string child, List<TreeNode> treeNodes) 
    {
        var treeNode = treeNodes?.Find(i => i.hash == parent);
        treeNode?.blobs?.Add(child);
    }

    static void CreateLinkNeo(ISession session, string parent, string child, string parentType, string childType)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:tree), (b:blob) WHERE t.hash ='{parent}' AND b.hash ='{child}' CREATE (t)-[blob_link:blob]->(b) RETURN type(blob_link)",
                new { });

            return result;
        });
    }

    static bool CreateHEADTOBranchLinkNeo(ISession session, string branchName)
    {

        //Console.WriteLine("HEAD -> " + branchName);
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:HEAD), (b:branch) WHERE t.name ='HEAD' AND b.name ='{branchName}' CREATE (t)-[head_link:HEAD]->(b) RETURN type(head_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }

    static bool CreateHEADTOCommitLinkNeo(ISession session, string childCommit)
    {
        //Console.WriteLine("HEAD -> " + childCommit);
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:HEAD), (b:commit) WHERE t.name ='HEAD' AND b.hash ='{childCommit}' CREATE (t)-[head_link:HEAD]->(b) RETURN type(head_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }

    static bool CreateCommitTOCommitLinkNeo(ISession session, string parent, string child)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:commit), (b:commit) WHERE t.hash ='{parent}' AND b.hash ='{child}' CREATE (t)-[parent_link:parent]->(b) RETURN type(parent_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }

    static bool CreateCommitLinkNeo(ISession session, string parent, string child, string parentType, string childType)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:commit), (b:tree) WHERE t.hash ='{parent}' AND b.hash ='{child}' CREATE (t)-[tree_link:tree]->(b) RETURN type(tree_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }

    static bool CreateRemoteBranchLinkNeo(ISession session, string parent, string child)
    {
        //Console.WriteLine($"Create Remote Branch link {parent} {child}");

        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:remotebranch), (b:commit) WHERE t.name ='{parent}' AND b.hash ='{child}' CREATE (t)-[remotebranch_link:branch]->(b) RETURN type(remotebranch_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }


    static bool CreateBranchLinkNeo(ISession session, string parent, string child)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                $"MATCH (t:branch), (b:commit) WHERE t.name ='{parent}' AND b.hash ='{child}' CREATE (t)-[branch_link:branch]->(b) RETURN type(branch_link)",
                new { });

            return result.Count();
        });

        return greeting > 0 ? true : false;
    }

   

    static void AddCommitToNeo(ISession session, string comment, string hash, string contents)
    {
        string name = $"commit #{hash} {comment}";

        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                "CREATE (a:commit) " +
                "SET a.name = $name " +
                "SET a.comment = $comment " +
                "SET a.contents = $contents " +
                "SET a.hash = $hash " +
                "RETURN a.name + ', from node ' + id(a)",
                new { comment, hash, name, contents });

            return "created node";
        });
    }

    static void AddBranchToJson(string name, string hash, List<Branch> branches)
    {
       Branch b = new Branch();
        b.hash = hash;
        b.name = name;

        if (!branches.Exists(i => i.hash == b.hash))
            branches.Add(b);
    }

    static void AddBranchToNeo(ISession session, string name, string hash)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                "CREATE (a:branch) " +
                "SET a.name = $name " +
                "SET a.hash = $hash " +
                "RETURN a.name + ', from node ' + id(a)",
                new { name, hash });

            return "created node";
        });
    }

    static void AddRemoteBranchToNeo(ISession session, string name, string hash)
    {
        name = $"remote{name}";

        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                "CREATE (a:remotebranch) " +
                "SET a.name = $name " +
                "SET a.hash = $hash " +
                "RETURN a.name + ', from node ' + id(a)",
                new { name, hash });

            return "created node";
        });
    }

   
  

    static void AddTreeToNeo(ISession session, string hash, string contents)
    {
        string name = $"tree #{hash}";

        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                "CREATE (a:tree) " +
                "SET a.name = $name " +
                "SET a.hash = $hash " +
                "SET a.contents = $contents " +
                "RETURN a.name + ', from node ' + id(a)",
                new { hash, contents, name });

            return "created node";
        });
    }

    static void AddHeadToNeo(ISession session, string hash, string contents)
    {
        var greeting = session.ExecuteWrite(
        tx =>
        {
            var result = tx.Run(
                "CREATE (a:HEAD) " +
                "SET a.name = 'HEAD' " +
                "SET a.hash = $hash " +
                "SET a.contents = $contents " +
                "RETURN a.name + ', from node ' + id(a)",
                new { hash, contents });

            return "created node";
        });
    }


 

   
    return true;
}
