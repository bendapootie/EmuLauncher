using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;

namespace emu_launcher
{
    public partial class Form1 : Form
    {
        // Path to executable to launch
        private const string ExeVariableName = "launch_exe";
        // Command line arguments to pass to executable
        private const string ArgsVariableName = "launch_args";
        // Working directory to start from
        private const string LaunchDirVariableName = "launch_dir";
        // How many milliseconds to wait before forcefully shutting down child process
        // Note: Negative value means only send close event and never kill the process
        private const string ShutdownTimeout = "shutdown_timeout";

        private const string Separator = "=";
        private const string VariableStartDelim = "$";
        private const string VariableEndDelim = "$";
        private List<string> CommentDelimiters = new List<string>
        {
            "//",
            ";",
            "#"
        };

        private string EmuLauncherExePath = "";
        private string ConfigPath = "";
        private Process ChildProcess = null;
        // Default to waiting 5-second for child process to end before killing
        private int TimeoutMilliseconds = 5000;
        // List of variables and their values
        private List<KeyValuePair<string, string>> Variables = new List<KeyValuePair<string, string>>();

        private List<string> Errors = new List<string>();
        private Stack<ParsingContext> Context = new Stack<ParsingContext>();

        private ParsingContext ActiveContext { get { return Context.Count > 0 ? Context.Peek() : null; } }

        private Timer UpdateTimer = new Timer();

        public Form1()
        {
            InitializeComponent();
            ParseCommandLine();
            AddDefaultVariables();
            ParseEmuFile(ConfigPath);
            LaunchProcess();
        }

        private void ParseCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            EmuLauncherExePath = args[0];
            if (args.Length > 1)
            {
                ConfigPath = args[1];
            }
            else
            {
                AddError("Expected the path to a '.emu' file to be given in the command line.");
            }
        }
        private void AddDefaultVariables()
        {
            Variables.Add(new KeyValuePair<string, string>(ExeVariableName, ""));
            Variables.Add(new KeyValuePair<string, string>(ArgsVariableName, ""));
            Variables.Add(new KeyValuePair<string, string>(LaunchDirVariableName, ""));
            Variables.Add(new KeyValuePair<string, string>("MachineName", System.Environment.MachineName));
            Variables.Add(new KeyValuePair<string, string>("UserName", System.Environment.UserName));
            Variables.Add(new KeyValuePair<string, string>("WorkingDir", System.IO.Directory.GetCurrentDirectory()));
            Variables.Add(new KeyValuePair<string, string>("EmuLauncherDir", System.IO.Path.GetDirectoryName(EmuLauncherExePath)));
            Variables.Add(new KeyValuePair<string, string>("ConfigFileDir", System.IO.Path.GetDirectoryName(ConfigPath)));
            Variables.Add(new KeyValuePair<string, string>(ShutdownTimeout, string.Format("{0}", TimeoutMilliseconds)));
        }

        private string GetVariableValue(string variableName)
        {
            string lowerVar = variableName.ToLower();
            foreach (var vars in Variables)
            {
                if (lowerVar == vars.Key.ToLower())
                {
                    return vars.Value;
                }
            }
            AddError(string.Format("Couldn't find entry for variable '{0}'", variableName));
            return "";
        }

        // Returns a copy of passed in string, but with all occurances of
        // variable names replaced with their values
        private string ReplaceVariables(string stringToParse)
        {
            int startDelim = stringToParse.IndexOf(VariableStartDelim);
            if (startDelim < 0)
            {
                return stringToParse;
            }

            int endDelim = stringToParse.IndexOf(VariableEndDelim, startDelim + 1);
            if (endDelim < 0)
            {
                AddError(string.Format("Couldn't find matching end variable deliminator '{0}' in string", VariableEndDelim));
                return stringToParse;
            }

            string variableName = stringToParse.Substring(startDelim + 1, endDelim - (startDelim + 1));
            string variableValue = GetVariableValue(variableName);

            string output = stringToParse.Substring(0, startDelim);
            output += variableValue;
            output += stringToParse.Substring(endDelim + 1);

            // Keep trying to replace variables until there aren't any more
            // Yes, this is tail recursion which can easily be rewritten to use a loop, but it's not hurting anything
            return ReplaceVariables(output);
        }

        private void ParseEmuFile(string path)
        {
            // Early-out if there are already errors
            if (Errors.Count > 0)
            {
                return;
            }

            string fullFileName = GetFullPath(path);
            ParsingContext context = new ParsingContext()
            {
                File = fullFileName,
                Line = 0
            };
            Context.Push(context);

            string[] lines = new string[0];
            if (System.IO.File.Exists(fullFileName))
            {
                string fileText = File.ReadAllText(fullFileName);
                lines = fileText.Split('\n');
            }
            else
            {
                AddError(string.Format("File doesn't exist - '{0}'", path));
            }

            foreach (string line in lines)
            {
                context.Line++;

                // Check if the line is a comment or empty
                string trimmed = line.Trim();

                // Skip lines that are zero length after being trimmed
                bool skip_line = trimmed.Length == 0;
                // Skip lines that start with a comment delimiter
                foreach (string delim in CommentDelimiters)
                {
                    if (trimmed.StartsWith(delim))
                    {
                        skip_line = true;
                        break;
                    }
                }

                // Early-out of this loop if the line should be skipped
                if (skip_line)
                {
                    continue;
                }

                int separatorIndex = line.IndexOf(Separator);
                if (separatorIndex < 0)
                {
                    AddError(string.Format("No separator '{1}' found", Separator));
                    break;
                }

                string variable = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();

                if (variable.Length == 0)
                {
                    AddError(string.Format("No variable set before separator '{1}'", Separator));
                    break;
                }
                int invalidIndex = variable.IndexOfAny(" $".ToCharArray());
                if (invalidIndex >= 0)
                {
                    AddError(string.Format("Inavlid character '{0}' in variable name", variable[invalidIndex]));
                    break;
                }

                string processedValue = ReplaceVariables(value);

                // Check if variable name is special
                ProcessKeyValuePair(variable, processedValue);
            }

            Context.Pop();
        }

        // Searches in the working directories for a file with the given path and returns the full path to the file
        string GetFullPath(string path)
        {
            List<string> roots = new List<string>();
            // 1. Look relative to the active context, if there is one
            if (ActiveContext != null)
            {
                roots.Add(System.IO.Path.GetDirectoryName(ActiveContext.File));
            }
            // 2. Look relative to the current working directory (CWD)
            roots.Add(System.IO.Directory.GetCurrentDirectory());

            // 3. Look relative to the EmuLauncher executable path
            roots.Add(System.IO.Path.GetDirectoryName(EmuLauncherExePath));

            // Note: If passed in path is an absolute path, System.IO.Path.Combine will handle it correctly

            bool foundPath = false;
            string outputPath = path;
            foreach (string root in roots)
            {
                string testPath = System.IO.Path.Combine(root, path);
                if (System.IO.File.Exists(testPath))
                {
                    foundPath = true;
                    outputPath = testPath;
                    break;
                }
            }
            if (foundPath == false)
            {
                AddError(string.Format("Unable to find file from path '{0}'", path));
            }
            return outputPath;
        }

        void AddError(string message)
        {
            string error_message = message;
            if (ActiveContext != null)
            {
                error_message = string.Format("{0} ({1}): {2}", ActiveContext.File, ActiveContext.Line, message);
            }
            Errors.Add(error_message);
        }

        void ProcessKeyValuePair(string key, string value)
        {
            string keyLower = key.ToLower();
            switch (keyLower)
            {
                case "include":
                    ParseEmuFile(value);
                    break;
                default:
                    var newEntry = new KeyValuePair<string, string>(key, value);
                    int foundIndex = Variables.FindIndex(v => v.Key.ToLower() == keyLower);
                    if (foundIndex >= 0)
                    {
                        // Entry already exists. Update it.
                        Variables[foundIndex] = newEntry;
                    }
                    else
                    {
                        // Entry didn't exist. Add a new one.
                        Variables.Add(newEntry);
                    }
                    break;
            }
        }

        private void HandleTick(Object o, EventArgs e)
        {
            if (ChildProcess != null)
            {
                // If there's a child process, but it's been closed, clean things up and shut down Emu Launcher
                if (ChildProcess.HasExited)
                {
                    ChildProcess.Close();
                    ChildProcess = null;
                    UpdateTimer.Stop();
                    this.Close();
                }
            }
        }

        private void LaunchProcess()
        {
            // Sanity check to make sure things are set up right
            string exe_path = GetVariableValue(ExeVariableName);

            if ((Errors.Count == 0) && (System.IO.File.Exists(exe_path) == false))
            {
                AddError(string.Format("Couldn't find program to launch {0} = '{1}'", ExeVariableName, exe_path));
            }
            
            string args = GetVariableValue(ArgsVariableName);
            string launch_dir = GetVariableValue(LaunchDirVariableName);
            string shutdown_string = GetVariableValue(ShutdownTimeout);
            // Note: An empty value here will fall back to using the default value of TimeoutMilliseconds
            if (shutdown_string.Length > 0)
            {
                if (Int32.TryParse(shutdown_string, out TimeoutMilliseconds) == false)
                {
                    AddError(string.Format("Failed to parse variable '{0}' = '{1}' into an integer", ShutdownTimeout, shutdown_string));
                }
            }

            if (ChildProcess != null)
            {
                AddError(string.Format("Child Process already exists '{0}'", ChildProcess.ProcessName));
            }

            // Write all variables to window
            string variable_debug = "";
            foreach (var keyValuePair in Variables)
            {
                variable_debug += keyValuePair.Key + " = " + keyValuePair.Value + System.Environment.NewLine;
            }
            VariablesText.Text = variable_debug;

            if (Errors.Count > 0)
            {
                string messageString = "";
                foreach (string error in Errors)
                {
                    messageString += error + System.Environment.NewLine;
                }
                MessageText.Text = messageString;
                MessageText.SelectionLength = 0;
                return;
            }

            ChildProcess = new Process();
            ChildProcess.StartInfo.FileName = exe_path;
            ChildProcess.StartInfo.Arguments = args;
            ChildProcess.StartInfo.WorkingDirectory = launch_dir;
            ChildProcess.Start();

            // Start a timer that checks if the child process is still open
            UpdateTimer.Tick += new EventHandler(HandleTick);
            UpdateTimer.Interval = 1000;
            UpdateTimer.Start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ChildProcess != null)
            {
                if (ChildProcess.HasExited == false)
                {
                    ChildProcess.CloseMainWindow();
                    if (TimeoutMilliseconds > 0)
                    {
                        bool exited_cleanly = ChildProcess.WaitForExit(TimeoutMilliseconds);
                        if (!exited_cleanly)
                        {
                            bool killEntireProcessTree = true;
                            ChildProcess.Kill(killEntireProcessTree);
                        }
                    }
                    ChildProcess.Close();
                    ChildProcess = null;
                }
            }
        }
    }

    public class ParsingContext
    {
        public string File;
        public int Line;
    }
}
