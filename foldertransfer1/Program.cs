using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace bakfolders
{
    internal class Program
    {
        private static string tasksFile = "tasks.txt";
        private static string logFile = "log.txt";
        private static string taskHistoryFile = "taskhistory.txt";
        private static string tasksResultFile = "taskresult.txt";

        //todo 新增任务后，会根据上次执行别的任务的时间，作为新增任务的时间，这样不对。新增任务应全部更新。

        static void Main(string[] args)
        {
            //命令行参数 -c 是否拷贝后比较源文件夹与目标文件夹差异，目标文件中没有的都拷贝过去。默认为false
            var checkResult = args.Any(b => b.Trim().Contains("-c"));
            //命令行参数 -h 显示帮助。默认为false
            var showHelp = args.Any(b => b.Trim().Contains("-h"));
            //命令行参数 -g 是否生成目标文件夹中的文件列表，以便拷贝前比对,也可省略此步骤直接执行。默认为false
            var generatetargetFoldersExitingFilesList = args.Any(b => b.Trim().Contains("-g"));

            if (showHelp)
            {
                ShowHelp();
                return;
            }

            if (!File.Exists(tasksFile))
            {
                File.Create(tasksFile).Close();
            }

            // tasks.txt文件中可加入注释。以"--"开头的行均为注释行
            var tasks = File.ReadAllLines(tasksFile).Where(p => p != "" && !p.Trim().StartsWith("--")).Select(p => p.Trim());

            List<string> exitingFiles = new List<string>();
            if (generatetargetFoldersExitingFilesList)
            {
                GenerateTasksResultFile(tasks);
                Console.WriteLine("生成目标文件夹文件列表taskresult.txt完毕！");
                return;
            }

            var startTime = DateTime.Now.ToString();
            Console.WriteLine("Task start(任务开始)！    " + startTime);

            var copiedFilesCount = CopyDirectories(tasks, checkResult);

            //保存目标文件夹文件列表及各文件写入时间结果
            GenerateTasksResultFile(tasks);

            var endTime = DateTime.Now.ToString();
            Console.WriteLine("Task finished(任务结束)！ " + endTime);
            Console.WriteLine("Task copyed files 拷贝文件数量： " + copiedFilesCount);
            WriteLogs(tasks, copiedFilesCount, startTime, endTime);
        }


        private static int CopyDirectories(IEnumerable<string> directories, bool checkresult)
        {
            int count = 0;

            // 保存结果
            if (!File.Exists(taskHistoryFile))
            {
                File.Create(taskHistoryFile).Close();
            }

            ParallelOptions parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = 4;

            Parallel.ForEach(directories, parallelOptions, d =>
            {
                int countD = 0;
                var allPath = d.Split('\t');
                var sourcePath = allPath[0].Trim();
                var targetPath = allPath[1].Trim();

                Console.WriteLine("Copy(拷贝)：    " + sourcePath + " --> " + targetPath);

                if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath) || string.IsNullOrEmpty(targetPath)) return;

                var taskStartTime = DateTime.Now.ToString();

                //获取上次拷贝任务，检查是否有相同任务
                var lastSameTask = File.ReadAllLines(taskHistoryFile).LastOrDefault(h => h.StartsWith(d));

                var allSourceFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

                IEnumerable<string> realShouldCopyFiles = Enumerable.Empty<string>();
                IEnumerable<string> existingTargetFiles = Enumerable.Empty<string>();

                //上次有相同任务，则上次任务后更新的文件全部拷贝，没有相同任务则全部拷贝
                // 这里读取文件列表的速度和从文件夹读取的速度，需要测试，估计差异较小
                if (File.Exists(tasksResultFile))
                {
                    existingTargetFiles = File.ReadAllLines(tasksResultFile).Where(f => f.StartsWith(targetPath));
                }
                else
                {
                    existingTargetFiles = GetexitingFiles(targetPath);
                }

                //  获取上次拷贝任务，检查是否有相同任务 如果有
                if (lastSameTask != null)
                {
                    var lasttaskTimestring = lastSameTask.Split('\t')[2];
                    var lasttaskTime = DateTime.Parse(lasttaskTimestring);
                    realShouldCopyFiles = allSourceFiles.Where(f => new FileInfo(f).LastWriteTime >= lasttaskTime);

                    Console.WriteLine(" 上次执行过相同任务时点后，需拷贝的新文件数量：   " + realShouldCopyFiles.Count());
                }
                else
                {
                    //  获取上次拷贝任务，检查是否有相同任务 如果没有
                    //  根据目标目录文件记录，生成源文件路径及目标文件时间
                    var existingsourcefileswithtime = existingTargetFiles.Select(f => f.Replace(targetPath, sourcePath));

                    // 获取源文件名称列表
                    var existingsourcefilenames = existingsourcefileswithtime.Select(f => f.Split('\t')[0]).ToList();

                    //源目录文件排除目标目录已有相同名称的文件，为新文件，需拷贝
                    var newFiles = allSourceFiles.Except(existingsourcefilenames);
                    realShouldCopyFiles.Concat(newFiles);
                    Console.WriteLine(" 比较目标文件夹文件，需拷贝新增文件数量：  " + newFiles.Count());

                    int changedFilesCount = 0;
                    foreach (var existingfilewithtime in existingsourcefileswithtime)
                    {
                        var existingSourceFileName = existingfilewithtime.Split('\t')[0];
                        var existingTargetFileTime = existingfilewithtime.Split('\t')[1];

                        if (new FileInfo(existingSourceFileName).LastWriteTime > DateTime.Parse(existingTargetFileTime).AddSeconds(1))
                        {
                            changedFilesCount++;
                            realShouldCopyFiles.Append(existingSourceFileName);
                        }
                    }
                    Console.WriteLine(" 比较目标文件夹文件，需拷贝更新文件数量：  " + changedFilesCount);
                }

                Parallel.ForEach(realShouldCopyFiles, parallelOptions, sourcefile =>
                {
                    string targetFilePath = sourcefile.Replace(sourcePath, targetPath);
                    if (!File.Exists(targetFilePath) || File.GetLastWriteTime(targetFilePath) < File.GetLastWriteTime(sourcefile))
                    {
                        if (!Directory.GetParent(targetFilePath).Exists) CreateDirectory(Directory.GetParent(targetFilePath).FullName);
                        try
                        {
                            File.Copy(sourcefile, targetFilePath, true);
                            countD++;
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
                Console.WriteLine(" 共拷贝文件数量：  " + countD);

                //保存任务历史
                File.AppendAllLines(taskHistoryFile, new List<string> { d + "\t" + taskStartTime });

                // 复查复制结果 拷贝目标文件夹中尚不存在的文件，达到全面相同的目标
                if (checkresult)
                {
                    var existingsourcefiles = existingTargetFiles.Select(f => f.Split('\t')[0].Replace(targetPath, sourcePath));

                    var lostfiles = allSourceFiles.Except(existingsourcefiles);

                    Console.WriteLine(" 共需拷贝丢失文件数量：  " + lostfiles.Count());

                    Parallel.ForEach(lostfiles, parallelOptions, sourcefile =>
                    {
                        string targetFilePath = sourcefile.Replace(sourcePath, targetPath);
                        if (!Directory.GetParent(targetFilePath).Exists) CreateDirectory(Directory.GetParent(targetFilePath).FullName);
                        try
                        {
                            File.Copy(sourcefile, targetFilePath, true);
                            countD++;
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
                count += countD;
            });
            return count;
        }

        private static void GenerateTasksResultFile(IEnumerable<string> tasks)
        {
            File.Create(tasksResultFile).Close();
            File.AppendAllLines(tasksResultFile, GetexitingFilesFromtasks(tasks));
        }

        private static IEnumerable<string> GetexitingFilesFromtasks(IEnumerable<string> tasks)
        {
            //保存已拷贝文件列表及文件变更时间
            IEnumerable<string> exitingFiles = Enumerable.Empty<string>();
            foreach (var path in tasks)
            {
                var targetpath = path.Split('\t')[1];
                exitingFiles.Concat(GetexitingFiles(targetpath));
            }
            return exitingFiles;
        }

        private static IEnumerable<string> GetexitingFiles(string path)
        {
            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories).Select(f => f.FullName + "\t" + f.LastWriteTime);
            }
            else
            {
                return Enumerable.Empty<string>();
            }
        }

        //循环创建父文件夹路径
        private static void CreateDirectory(string directoryname)
        {
            if (Directory.GetParent(directoryname).Exists)
            {
                Directory.CreateDirectory(directoryname);
                return;
            }
            var parentPath = Directory.GetParent(directoryname).FullName;
            if (!Directory.Exists(parentPath))
            {
                CreateDirectory(parentPath);
                Directory.CreateDirectory(parentPath);
            }
            Directory.CreateDirectory(directoryname);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("使用帮助：\n" +
                    "操作顺序：\n" +
                    "1.在本程序相同目录下的tasks.txt文件中设置文件夹拷贝任务，一行一个任务，可以多行，每行格式为：\n" +
                    "源文件夹完整路径   目标文件夹完整路径\n" +
                    "中间用tab分割。\n\n" +
                    "2.第一次执行，建议首先生成目标文件夹文件列表taskresult.txt，加快比较速度，命令如下： \n" +
                    "bakfolders.exe -g\n" +
                    "后续每次任务结束会自动更新该文件，以供下次比较使用。\n\n" +
                    "3.双击或命令行执行 bakfolders.exe \n" +
                    "命令如下： \n" +
                    "bakfolders.exe\n" +
                    "命令行执行可查看执行结果\n" +
                    "4.命令执行完毕，会追加日志到 log.txt，记录任务历史到任务历史文件 taskhistory.txt\n" +
                    "若存在任务历史文件，则下次执行命令时会检查相同任务上次执行的时点，只拷贝该时点后更新或新增的文件到目标文件夹\n" +
                    "5.天天开心！ \n\n" +
                    "审计信息化QQ群: 256912702\n"
                );
        }

        private static void WriteLogs(IEnumerable<string> paths, int count, string startTime, string endTime)
        {
            if (!File.Exists(logFile))
            {
                File.Create(logFile).Close();
            }

            var logText = new List<string>()
            {
                "\n\n------------------------------* Task Start *------------------------------",
                "--------------- StartTime: " + startTime + "   --------",
            };
            foreach (var path in paths)
            {
                logText.Add("--------------- Copy:   " + path + "   ------");
            };
            logText.Add("--------------- EndTime:   " + endTime + "   --------");
            logText.Add("--------------- " + count + " Files Copied.    ---------------");
            logText.Add("------------------------------* Task End   *------------------------------\n\n");

            File.AppendAllLines(logFile, logText);
        }

        //static int CopyDirectory(string sourceDir, string destinationDir)
        //{
        //    int count1 = 0;
        //    // Get information about the source directory
        //    var sourcedir = new DirectoryInfo(sourceDir);
        //    if (!sourcedir.Exists) return 0;

        //    Directory.CreateDirectory(destinationDir);

        //    ParallelOptions parallelOptions = new ParallelOptions();
        //    parallelOptions.MaxDegreeOfParallelism = 4;
        //    Parallel.ForEach(sourcedir.GetFiles(), parallelOptions, sourcefile =>
        //    {
        //        string targetFilePath = Path.Combine(destinationDir, sourcefile.Name);
        //        if (!File.Exists(targetFilePath) || File.GetLastWriteTime(targetFilePath) < sourcefile.LastWriteTime)
        //        {
        //            try
        //            {
        //                sourcefile.CopyTo(targetFilePath, true);
        //                count1++;
        //            }
        //            catch (Exception)
        //            {
        //            }
        //        }
        //    });

        //    Parallel.ForEach(sourcedir.GetDirectories(), parallelOptions, subDir =>
        //    {
        //        string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
        //        count1 += CopyDirectory(subDir.FullName, newDestinationDir);
        //    });

        //    return count1;
        //}
    }

}

