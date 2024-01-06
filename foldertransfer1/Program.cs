using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace bakfolders
{
    internal class Program
    {
        private static string tasksFile = "[tasks].txt";
        private static string logFile = "log.txt";
        private static string taskHistoryFile = "taskshistory.txt";
        private static string tasksResultFile = "tasksresult.txt";

        static void Main(string[] args)
        {
            //命令行参数 -c 是否拷贝后比较源文件夹与目标文件夹差异，目标文件中没有的都拷贝过去。默认为false
            var checkResult = args.Any(b => b.Trim().Contains("-c"));

            //命令行参数 -h 显示帮助。默认为false
            var showHelp = args.Any(b => b.Trim().Contains("-h"));

            //命令行参数 -g 是否生成目标文件夹中的文件列表，以便拷贝前比对,也可省略此步骤直接执行。默认为false
            var generateTargetFoldersExitingFilesList = args.Any(b => b.Trim().Contains("-g"));

            if (showHelp)
            {
                ShowHelp();
                return;
            }

            if (!File.Exists(tasksFile))
            {
                File.Create(tasksFile).Close();
            }
            if (!File.Exists(taskHistoryFile))
            {
                File.Create(taskHistoryFile).Close();
            }

            // tasks.txt文件中可加入注释。以"--"开头的行均为注释行
            var tasks = File.ReadAllLines(tasksFile).Where(p => p != "" && !p.Trim().StartsWith("--")).Select(p => p.Trim());

            if (generateTargetFoldersExitingFilesList)
            {
                GenerateTasksResultFile(tasks);
                Console.WriteLine("生成目标文件夹文件列表taskresult.txt完毕！");
                return;
            }

            var startTime = DateTime.Now.ToString();
            Console.WriteLine("*** 任务开始时间：" + startTime);

            var copiedFilesCount = CopyDirectories(tasks, checkResult);

            //保存目标文件夹文件列表及各文件写入时间结果,比较耗时，不执行了
            //GenerateTasksResultFile(tasks);

            var endTime = DateTime.Now.ToString();
            WriteLogs(tasks, copiedFilesCount, startTime, endTime);

            Console.WriteLine("*** 任务结束时间：" + endTime);
            Console.WriteLine("全部任务合计拷贝文件总数量：    " + copiedFilesCount);
            //Console.WriteLine("点击任意键退出!");
            //Console.ReadKey();
        }

        private static int CopyDirectories(IEnumerable<string> directories, bool checkresult)
        {
            int count = 0;

            ParallelOptions parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = 4;

            foreach (var directory in directories)
            {
                int countD = 0;
                var allPath = directory.Split('\t');
                var sourcePath = allPath[0].Trim();
                var targetPath = allPath[1].Trim();

                if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath) || !Directory.Exists(sourcePath)) continue;

                var taskStartTime = DateTime.Now.ToString();

                //获取上次拷贝任务，检查是否有相同任务
                var lastSameTask = File.ReadAllLines(taskHistoryFile).LastOrDefault(h => h.StartsWith(directory));

                var allSourceFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

                var allSourceFilesCount = allSourceFiles.Count();

                if (!Directory.Exists(targetPath))
                {
                    //若目标文件夹不存在，则直接复制
                    Console.WriteLine(sourcePath + " --> " + targetPath + "   全新拷贝该文件夹下全部文件数量：" + allSourceFilesCount);
                    count += CopyDirectory(sourcePath, targetPath);
                    Console.WriteLine(sourcePath + " --> " + targetPath + "   完成拷贝文件数量：" + count);

                    //保存任务历史
                    File.AppendAllLines(taskHistoryFile, new List<string> { directory + "\t" + taskStartTime });
                    continue;
                }

                //todo 拷贝文件为空，需检查原因
                IEnumerable<string> realShouldCopyFiles = Enumerable.Empty<string>();
                IEnumerable<string> existingTargetFiles = Enumerable.Empty<string>();

                //上次有相同任务，则上次任务后更新的文件全部拷贝，没有相同任务则全部拷贝
                // 这里读取文件列表的速度和从文件夹读取文件列表的速度，需要测试，估计差异较小
                //if (File.Exists(tasksResultFile))
                //{
                //    existingTargetFiles = File.ReadAllLines(tasksResultFile).Where(f => f.StartsWith(targetPath)).AsParallel();
                //}
                //else
                //{
                existingTargetFiles = GetExitingFiles(targetPath);
                //}

                //  获取上次拷贝任务，检查是否有相同任务 如果有则只拷贝上次任务后更新的文件 
                if (lastSameTask != null)
                {
                    var lasttaskTimestring = lastSameTask.Split('\t')[2];
                    var lasttaskTime = DateTime.Parse(lasttaskTimestring);

                    realShouldCopyFiles = allSourceFiles.AsParallel().Where(f => new FileInfo(f).LastWriteTime >= lasttaskTime);

                    //检查并拷贝目标文件夹中不存在，原文件夹存在的文件 可以加命令行参数 -c 选择性执行，此处为提高效率，不每次执行
                    //checkresult = true;
                }
                else
                {
                    // todo 从源路径比较 待研究
                    //var existingSourceFileNamesWithTime = GetExitingFiles(sourcePath);

                    //foreach (var file in existingSourceFileNamesWithTime.Except(existingTargetFiles)
                    //{

                    //}

                    //  获取上次拷贝任务，检查是否有相同任务 如果没有相同任务执行过 则检查已拷贝文件列表，拷贝新文件及更新的文件
                    //  根据目标目录文件列表，生成源文件名称及目标文件更新时间列表
                    var generatedSourceFileNamesWithTime = existingTargetFiles.Select(f => f.Replace(targetPath, sourcePath)).AsParallel();

                    // 生成源文件名称列表
                    var generatedSourceFileNames = generatedSourceFileNamesWithTime.Select(f => f.Split('\t')[0]);

                    //源目录全部文件排除目标目录已有相同名称的文件，为新文件，需全部拷贝
                    var newFiles = allSourceFiles.Except(generatedSourceFileNames);
                    realShouldCopyFiles = realShouldCopyFiles.Concat(newFiles);

                    //老文件,则比较文件更新时间，更新时间大，则加入更新列表
                    Parallel.ForEach(generatedSourceFileNamesWithTime, parallelOptions, fwt =>
                    {
                        var existingSourceFileName = fwt.Split('\t')[0];
                        var existingTargetFileTime = fwt.Split('\t')[1];
                        var lastWriteTime = DateTime.Parse(existingTargetFileTime).AddSeconds(1);
                        var f = new FileInfo(existingSourceFileName);

                        if (f.LastWriteTime > lastWriteTime)
                        {
                            realShouldCopyFiles.Append(existingSourceFileName);
                        }
                    });
                }

                Console.WriteLine(sourcePath + " --> " + targetPath + "   全部原始文件数量：" + allSourceFilesCount);
                Console.WriteLine(sourcePath + " --> " + targetPath + "   准备拷贝文件数量：" + realShouldCopyFiles.Count());

                Parallel.ForEach(realShouldCopyFiles, parallelOptions, sourcefile =>
                {
                    string targetFilePath = sourcefile.Replace(sourcePath, targetPath);
                    if (!File.Exists(targetFilePath) || File.GetLastWriteTime(targetFilePath) < File.GetLastWriteTime(sourcefile))
                    {
                        if (!Directory.GetParent(targetFilePath).Exists) CreateDirectory(Directory.GetParent(targetFilePath).FullName);
                        try
                        {
                            File.Copy(sourcefile, targetFilePath, true);
                            Interlocked.Add(ref countD, 1);
                        }
                        catch (Exception)
                        {
                        }
                    }
                });

                Console.WriteLine(sourcePath + " --> " + targetPath + "   完成拷贝文件数量：" + countD);

                // 复查复制结果 拷贝目标文件夹中尚不存在的文件，达到全面相同的目标
                if (checkresult)
                {
                    var shouldExistSourceFiles = existingTargetFiles.Select(f => f.Split('\t')[0].Replace(targetPath, sourcePath)).AsParallel();

                    var notExistFiles = allSourceFiles.AsParallel().Except(shouldExistSourceFiles);

                    Console.WriteLine(" 全部检查，共需拷贝目标文件夹不存在的文件数量：  " + notExistFiles.Count());

                    Parallel.ForEach(notExistFiles, parallelOptions, sourceFilePath =>
                    {
                        string targetFilePath = sourceFilePath.Replace(sourcePath, targetPath);
                        if (!Directory.GetParent(targetFilePath).Exists) CreateDirectory(Directory.GetParent(targetFilePath).FullName);
                        try
                        {
                            File.Copy(sourceFilePath, targetFilePath, true);
                            Interlocked.Add(ref countD, 1);
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
                //保存任务历史
                File.AppendAllLines(taskHistoryFile, new List<string> { directory + "\t" + taskStartTime });
                count += countD;
            }
            return count;
        }

        private static void GenerateTasksResultFile(IEnumerable<string> tasks)
        {
            File.Create(tasksResultFile).Close();
            File.AppendAllLines(tasksResultFile, GetExitingFilesForTasks(tasks));
            Console.WriteLine("目标文件夹共有文件数量：" + GetExitingFilesForTasks(tasks).Count());
        }

        private static IEnumerable<string> GetExitingFilesForTasks(IEnumerable<string> tasks)
        {
            //保存已拷贝文件列表及文件变更时间
            IEnumerable<string> exitingFiles = Enumerable.Empty<string>();
            foreach (var path in tasks)
            {
                var targetpath = path.Split('\t')[1];
                exitingFiles = exitingFiles.Concat(GetExitingFiles(targetpath));
            }
            return exitingFiles;
        }

        private static IEnumerable<string> GetExitingFiles(string path)
        {
            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories).Select(f => f.FullName + "\t" + f.LastWriteTime).AsParallel();
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
            Console.WriteLine("\n\n\t\t 使用帮助（建议操作顺序）：\n\n" +
                    "1.在本程序相同目录下的[tasks].txt文件中设置文件夹拷贝任务，一行一个任务，可以多行，每行格式示例：\n" +
                    "D:\\source\\path   E:\\target\\path\n" +
                    "中间用tab分割。\n\n" +
                    "2.第一次执行，建议首先生成目标文件夹文件列表taskresult.txt，加快比较速度（也可不执行），命令如下： \n" +
                    "bakfolders -g\n" +
                    "后续每次任务结束会自动更新taskresult.txt文件，以供下次比较使用。此命令不需多次执行。\n\n" +
                    "3.日常备份中，双击bakfolders.exe文件或命令行执行，命令如下： \n" +
                    "bakfolders\n\n" +
                    "4.命令执行完毕，会追加日志到 log.txt，记录任务历史到任务历史文件 taskhistory.txt\n" +
                    "若历史文件中存在相同任务，则下次执行命令时会检查相同任务上次执行的时点，只拷贝该时点后更新或新增的文件，\n" +
                    "所以可能存在新增的该时点前的文件不会被拷贝的情况！\n\n" +
                    "5.若需全面检查执行目标文件夹并拷贝目标文件夹中不存在的文件，即不以上次任务时间为增量备份的标准，\n" +
                    "以达到充分拷贝全部文件的目的，则增加拷贝后检查步骤，命令如下： \n" +
                    "bakfolders -c\n\n" +
                    " * 天天开心 ：） \n\n" +
                    "审计信息化QQ群: 256912702\n\n"
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

        static int CopyDirectory(string sourceDir, string destinationDir)
        {
            int filesCopiedCount = 0;
            // Get information about the source directory
            var sourcedir = new DirectoryInfo(sourceDir);
            if (!sourcedir.Exists) return 0;

            Directory.CreateDirectory(destinationDir);

            ParallelOptions parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = 4;
            Parallel.ForEach(sourcedir.GetFiles(), parallelOptions, sourcefile =>
            {
                string targetFilePath = Path.Combine(destinationDir, sourcefile.Name);
                try
                {
                    sourcefile.CopyTo(targetFilePath, true);
                    //并行中新增计数1
                    Interlocked.Add(ref filesCopiedCount, 1);
                }
                catch (Exception)
                {
                }
            });

            Parallel.ForEach(sourcedir.GetDirectories(), parallelOptions, subDir =>
            {
                string subDestinationDir = Path.Combine(destinationDir, subDir.Name);
                Interlocked.Add(ref filesCopiedCount, CopyDirectory(subDir.FullName, subDestinationDir));
            });

            return filesCopiedCount;
        }
    }
}

