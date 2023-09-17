﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using Neo4j.Driver;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Timers;
using System.Text;
using RandomNameGeneratorLibrary;
using System.ComponentModel.Design.Serialization;

object MainLockObj = new Object();
bool firstRun = true;
int batch = 1;
int dataID = 1;
var personGenerator = new PlaceNameGenerator();
var name = personGenerator.GenerateRandomPlaceName();
object balanceLock = new object();

bool EmitJsonOnly = false; ;
bool EmitWeb = false; ;
bool EmitNeo = false;
bool BatchingUpFileChanges = false;

bool debug = false;

string testPath = @"";

if (debug) {
    testPath = @"C:\dev\remote";
}
//string UserProfileFolder = Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
string UserProfileFolder = @"C:\github\gitgraph\src";
string CommitNodesJsonFile = Path.Combine(UserProfileFolder,"Json", "CommitGitInJson.json");
string TreeNodesJsonFile = Path.Combine(UserProfileFolder,"Json", "TreeGitInJson.json");
string BlobNodesJsonFile = Path.Combine(UserProfileFolder,"Json", "BlobGitInJson.json");
string HeadNodesJsonFile = Path.Combine(UserProfileFolder,"Json", "HeadGitInJson.json");
string BranchNodesJsonFile = Path.Combine(UserProfileFolder,"Json", "BranchGitInJson.json");
string IndexFilesJsonFile = Path.Combine(UserProfileFolder,"Json", "IndexfilesGitInJson.json");
string WorkingFilesJsonFile = Path.Combine(UserProfileFolder,"Json", "WorkingfilesGitInJson.json");

string workingArea = Path.Combine(testPath, @".\");
string head = Path.Combine(testPath, @".git\");
string path = Path.Combine(testPath, @".git\objects\");
string branchPath = Path.Combine(testPath, @".git\refs\heads");
string remoteBranchPath = Path.Combine(testPath, @".git\refs\remotes");

if (debug)
{
     workingArea = testPath;
     head = Path.Combine(testPath, @".git\");
     path = Path.Combine(testPath, @".git\objects\");
     branchPath = Path.Combine(testPath, @".git\refs\heads");
     remoteBranchPath = Path.Combine(testPath, @".git\refs\remotes");
}
else
{
     workingArea = Path.Combine(testPath, @".\");
     head = Path.Combine(testPath, @".git\");
     path = Path.Combine(testPath, @".git\objects\");
     branchPath = Path.Combine(testPath, @".git\refs\heads");
     remoteBranchPath = Path.Combine(testPath, @".git\refs\remotes");
}

if (args?.Length > 0 || debug)
{
    if (debug) {
            EmitWeb = true;
            Console.WriteLine("Emitting to Website");
    }
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
            Console.WriteLine("Emitting Json");
        }

         if (args?[0] == "--web")
        {
            EmitWeb = true;
            Console.WriteLine("Emitting to Website");
        }

        if (args?[0] == "--neo")
        {
            EmitNeo = true;
            Console.WriteLine("Emitting to Website");
        }
    }
}


string MyExePath = Environment.CurrentDirectory;
//string MyExePath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;

// Cant use this in single file publish ;
string MyExeFolder;

if (!debug) {
    MyExePath = Environment.CurrentDirectory;
    MyExeFolder = System.IO.Path.GetDirectoryName(MyExePath);
    MyExeFolder = MyExeFolder.Replace(@"file:\", "");
}
else {
    MyExePath = @"C:\dev\test";
    MyExeFolder = MyExePath;
    Console.WriteLine($"Debug: Using {MyExeFolder}");
}



// var builder = new ConfigurationBuilder()
//                                .SetBasePath(MyExeFolder)
//                                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

string password = "";
string uri = "";
string username = ""; 

// string password = builder.Build().GetSection("docker").GetSection("password").Value;
// string uri = builder.Build().GetSection("docker").GetSection("url").Value;
// string username = builder.Build().GetSection("docker").GetSection("username").Value;

// string password = builder.Build().GetSection("cloud").GetSection("password").Value;
// string uri = builder.Build().GetSection("cloud").GetSection("url").Value;
// string username = builder.Build().GetSection("cloud").GetSection("username").Value;

List<string> HashCodeFilenames = new List<string>();

// Make one run to start with before waiting for files to change
main();

using var watcher = new FileSystemWatcher(MyExeFolder);
{
    watcher.NotifyFilter = NotifyFilters.Attributes
                            | NotifyFilters.CreationTime
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.FileName
                            | NotifyFilters.LastAccess
                            | NotifyFilters.LastWrite
                            | NotifyFilters.Security
                            | NotifyFilters.Size;

    watcher.Changed += OnChanged;
    watcher.Created += OnChanged;
    watcher.Deleted += OnChanged;
    watcher.Renamed += OnChanged;
    //watcher.Error += OnError;

    watcher.Filter = "*.*";
    watcher.IncludeSubdirectories = true;
    watcher.EnableRaisingEvents = true;

    Console.ReadLine();
}

async void OnChanged(object sender, FileSystemEventArgs e)
{
    //Console.WriteLine(e.Name);

    if (e.Name.Contains(".lock", StringComparison.CurrentCultureIgnoreCase) ||
     e.Name.Contains("tmp", StringComparison.CurrentCultureIgnoreCase))
    {
        return;
    }
    
    if (!BatchingUpFileChanges)
    {
        BatchingUpFileChanges = true;
    
            var t = Task.Run(delegate
            {
                lock (MainLockObj)
                {
                    batch++;

                    Console.WriteLine($"Batch {batch} Waiting for file changes to complete.....");
                    Thread.Sleep(2000);
                    BatchingUpFileChanges = false;

                    Console.WriteLine($"Batch {batch} Processing.....");
                    main();
                    Console.WriteLine($"Batch {batch} Completed.....");
                }
                
            });
        
    }
    else {
        Console.WriteLine($"Batch {batch} batching " + e.Name);
    }
}


void main()
{
   

        List<CommitNode> CommitNodes = new List<CommitNode>();
        List<TreeNode> TreeNodes = new List<TreeNode>();
        List<Blob> blobs = new List<Blob>();
        List<Branch> branches = new List<Branch>();
        List<Branch> remoteBranches = new List<Branch>();


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

            if (EmitNeo)
            {
                _driver = GetDriver(uri, username, password);
                session = _driver.Session();
                ClearExistingNodesInNeo(session);
            }


            foreach (string dir in directories)
            {
                files = Directory.GetFiles(dir).ToList();

                foreach (string file in files)
                {
                    string hashCode = Path.GetFileName(dir) + Path.GetFileName(file).Substring(0, 2);

                    HashCodeFilenames.Add(hashCode);

                    string fileType = FileType.GetFileType(hashCode, workingArea);

                    if (fileType.Contains("commit"))
                    {
                        ////Console.WriteLine($"{fileType.TrimEnd('\n', '\r')} {hashCode}");

                        string commitContents = FileType.GetContents(hashCode, workingArea);
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

                            if (EmitNeo)
                            {
                                AddCommitToNeo(session, comment, hashCode, commitContents);
                            }

                            if (EmitNeo && !FileType.DoesNodeExistAlready(session, treeHash, "tree"))
                            {
                                if (EmitNeo)
                                    AddTreeToNeo(session, treeHash, FileType.GetContents(treeHash, workingArea));
                            }

                            CreateTreeJson(treeHash, FileType.GetContents(treeHash, workingArea), TreeNodes);
                            CreateCommitJson(parentHash, comment, hashCode, treeHash, commitContents, CommitNodes);

                            if (EmitNeo)
                            {
                                CreateCommitLinkNeo(session, hashCode, treeHash, "", "");
                            }

                            // Get the details of the Blobs in this Tree
                            string tree = FileType.GetContents(match.Groups[1].Value, workingArea);
                            var blobsInTree = Regex.Matches(tree, @"blob ([0-9a-f]{4})[0-9a-f]{36}.([\w\.]+)");

                            foreach (Match blobMatch in blobsInTree)
                            {
                                string blobHash = blobMatch.Groups[1].Value;
                                string blobContents = FileType.GetContents(blobHash, workingArea);

                                //Console.WriteLine($"\t\t-> blob {blobHash} {blobMatch.Groups[2]}");
                                if (EmitNeo && !FileType.DoesNodeExistAlready(session, blobHash, "blob"))
                                {
                                    if (EmitNeo)
                                        BlobCode.AddBlobToNeo(session, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents);
                                }
                                //Console.WriteLine($"Adding non orphan blob {blobMatch.Groups[1].Value}");
                                BlobCode.AddBlobToJson(treeHash, blobMatch.Groups[2].Value, blobMatch.Groups[1].Value, blobContents, blobs);

                                if (EmitNeo && !DoesTreeToBlobLinkExist(session, match.Groups[1].Value, blobHash))
                                {
                                    if (EmitNeo)
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
                var branchHash = File.ReadAllText(file);
                if (EmitNeo)
                {
                    AddBranchToNeo(session, Path.GetFileName(file), branchHash);
                    CreateBranchLinkNeo(session, Path.GetFileName(file), branchHash.Substring(0, 4));
                }
                AddBranchToJson(Path.GetFileName(file), branchHash.Substring(0, 4), branches);
            }

            // Add the Remote Branches
            foreach (var file in remoteBranchFiles)
            {
                var branchHash = File.ReadAllText(file);
                if (EmitNeo)
                {
                    AddRemoteBranchToNeo(session, Path.GetFileName(file), branchHash);
                    CreateRemoteBranchLinkNeo(session, $"remote{Path.GetFileName(file)}", branchHash.Substring(0, 4));
                }
                AddBranchToJson(Path.GetFileName(file), branchHash.Substring(0, 4), remoteBranches);

            }
            if (EmitNeo)
            {
                AddCommitParentLinks(session, path, workingArea);
                BlobCode.AddOrphanBlobs(session, branchPath, path, blobs, workingArea);
                GetHEAD(session, head);
            }


            if (EmitJsonOnly)
            {
                BlobCode.AddOrphanBlobsToJson(branchPath, path, blobs, workingArea);
                OutputNodesJson(CommitNodes, CommitNodesJsonFile);
                OutputNodesJson(TreeNodes, TreeNodesJsonFile);
                OutputNodesJson(blobs, BlobNodesJsonFile);
                OutputHEADJson(HEAD, HeadNodesJsonFile, head);
                OutputBranchJson(branches, TreeNodes, blobs, BranchNodesJsonFile);
                OutputIndexFilesJson(IndexFilesJsonFile);
                OutputWorkingFilesJson(workingArea, WorkingFilesJsonFile);
            }

            if (EmitWeb)
            {
                BlobCode.AddOrphanBlobsToJson(branchPath, path, blobs, workingArea);
                OutputNodesJsonToAPI(firstRun, name, dataID++, CommitNodes, blobs, TreeNodes, branches, remoteBranches, IndexFilesJsonNodes(workingArea), WorkingFilesNodes(workingArea), HEADNodes(head));
            }

            // Only run this on the first run
            if (firstRun)
            {
                firstRun = false;
                Process.Start(new ProcessStartInfo($"https://visualgit.net/visualize?data={name.Replace(' ', 'x')}/1") { UseShellExecute = true });
            }
        }
        catch (Exception e)
        {
            if (e.Message.Contains($"Could not find a part of the path"))
            {
                Console.WriteLine("Waiting for Git to be initiased in this folder...");
            }
            else
            {
                Console.WriteLine($"Error while getting files in {path} {e.Message} {e}");
            }
        }

    
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

    static HEAD HEADNodes(string path)
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
        return h;

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

     static List<WorkingFile> WorkingFilesNodes(string workingFolder) {
        
        List<string> files = FileType.GetWorkingFiles(workingFolder);
        List<WorkingFile> WorkingFilesList = new List<WorkingFile>();


        foreach (string file in files)
        {
            WorkingFile FileObj = new WorkingFile();
            FileObj.filename = file;
            FileObj.contents = FileType.GetFileContents(Path.Combine(workingFolder, file));
            WorkingFilesList.Add(FileObj);
        }
        return WorkingFilesList;
    }

    static void OutputWorkingFilesJson(string workingFolder, string JsonPath) {
        var Json = string.Empty;
        List<WorkingFile> WorkingFilesList = new List<WorkingFile>();

        List<string> files = FileType.GetWorkingFiles(workingFolder);

        foreach (string file in files)
        {
            WorkingFile FileObj = new WorkingFile();
            FileObj.filename = file;
            FileObj.contents = FileType.GetFileContents(Path.Combine(workingFolder, file));
            WorkingFilesList.Add(FileObj);
        }
        
        Json = JsonSerializer.Serialize(WorkingFilesList);
        File.WriteAllText(JsonPath, Json);
    }

    static List<IndexFile> IndexFilesJsonNodes(string workingArea)
    {
        var Json = string.Empty;
        List<IndexFile> IndexFilesList = new List<IndexFile>();

        string files = FileType.GetIndexFiles(workingArea);
       // Console.WriteLine(files);
        List<string> fileList = files.Split("\n").ToList();

        foreach (string file in fileList)
        {
            IndexFile FileObj = new IndexFile();
            FileObj.filename = file;
            IndexFilesList.Add(FileObj);
        }

        return IndexFilesList;

    }

    static void OutputIndexFilesJson(string JsonPath)
    {
        var Json = string.Empty;
        List<IndexFile> IndexFilesList = new List<IndexFile>();

        string files = FileType.GetIndexFiles("");
        //Console.WriteLine(files);
        List<string> fileList = files.Split("\n").ToList();

        foreach (string file in fileList)
        {
            IndexFile FileObj = new IndexFile();
            FileObj.filename = file;
            IndexFilesList.Add(FileObj);
        }

        Json = JsonSerializer.Serialize(IndexFilesList);

        //Console.WriteLine(Json);
        File.WriteAllText(JsonPath, Json);
    }


    static async Task PostAsync(bool firstrun, string name, int dataID,HttpClient httpClient, string commitjson, string blobjson, string treejson, string branchjson, string remotebranchjson, string indexfilesjson, string workingfilesjson, string HEADjson)
    {
        if (firstrun)
        Console.WriteLine($"Visual Git ID:  {name}"); //Outputs some random first and last name combination in the format "{first} {last}" example: "Mark Rogers"

        using StringContent jsonContent = new(
            JsonSerializer.Serialize(new
                    {
                        userId = $"{name.Replace(' ', 'x')}",
                        id = $"{dataID++}",
                        commitNodes = commitjson ?? "",
                        blobNodes = blobjson ?? "",
                        treeNodes = treejson ?? "",
                        branchNodes = branchjson ?? "",
                        remoteBranchNodes = remotebranchjson ?? "",
                        headNodes = HEADjson ?? "",
                        indexFilesNodes = indexfilesjson ?? "",
                        workingFilesNodes = workingfilesjson ?? ""
                    }),
                Encoding.UTF8,
                "application/json");

        using HttpResponseMessage response = await httpClient.PostAsync(
            "GitInternals",
            jsonContent);

            try {
                response.EnsureSuccessStatusCode();
                 var jsonResponse = await response.Content.ReadAsStringAsync();
            }
            catch(Exception ex) {
                Console.WriteLine(ex.Message);
            Console.WriteLine("Please restart VisualGit...");
            }
    }
    
    static async void OutputNodesJsonToAPI(bool firstrun, string name, int dataID, List<CommitNode> CommitNodes,
     List<Blob> BlobNodes, List<TreeNode> TreeNodes, List<Branch> BranchNodes, List<Branch> RemoteBranchNodes,
     List<IndexFile> IndexFilesNodes, List<WorkingFile> WorkingFilesNodes, HEAD HEADNodes)
    {
        var Json = string.Empty;

        var CommitJson = JsonSerializer.Serialize(CommitNodes);
        var BlobJson = JsonSerializer.Serialize(BlobNodes);
        var TreeJson = JsonSerializer.Serialize(TreeNodes);
        var BranchJson = JsonSerializer.Serialize(BranchNodes);
        var RemoteBranchJson = JsonSerializer.Serialize(RemoteBranchNodes);
        var IndexFilesJson = JsonSerializer.Serialize(IndexFilesNodes);
        var WorkingFilesJson = JsonSerializer.Serialize(WorkingFilesNodes);
        var HEADJson = JsonSerializer.Serialize(HEADNodes);

         HttpClient sharedClient = new()
        {
            BaseAddress = new Uri("https://gitvisualiserapi.azurewebsites.net/api/gitinternals"),
        };
        await PostAsync(firstrun, name, dataID, sharedClient, CommitJson, BlobJson, TreeJson, BranchJson, RemoteBranchJson, IndexFilesJson, WorkingFilesJson, HEADJson);
    }

    static void OutputNodesJson<T>(List<T> Nodes, string JsonPath)
    {
        var Json = string.Empty;

        Json = JsonSerializer.Serialize(Nodes);

        //Console.WriteLine(Json);
        //Console.WriteLine(JsonPath);
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




    static void AddCommitParentLinks(ISession session, string path, string workingArea)
    {
        List<string> directories = Directory.GetDirectories(path).ToList();

        foreach (string dir in directories)
        {
            var files = Directory.GetFiles(dir).ToList();

            foreach (string file in files)
            {

                string hashCode = Path.GetFileName(dir) + Path.GetFileName(file).Substring(0, 2);
                string fileType = FileType.GetFileType(hashCode, workingArea);

                if (fileType.Contains("commit"))
                {
                    string commitContents = FileType.GetContents(hashCode, workingArea);
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

        if (!branches.Exists(i => i.name == b.name))
        {
            //Console.WriteLine($"Adding branch {b.name} {b.hash}");
            branches.Add(b);
        }
        
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


 

   

