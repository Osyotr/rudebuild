﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.CommandBars;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;
using RudeBuildVSShared;

namespace RudeBuildVSIX
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class CommandHandler : ICommandRegistrar
    {
        // Command IDs. Have to match the IDs in the .vsct file!
        public const int CommandId_BuildSolution = 0x0100;
        public const int CommandId_RebuildSolution = 0x0102;
        public const int CommandId_CleanSolution = 0x0103;
        public const int CommandId_CleanCache = 0x0104;
        public const int CommandId_BuildProject = 0x0105;
        public const int CommandId_RebuildProject = 0x0106;
        public const int CommandId_CleanProject = 0x0107;
        public const int CommandId_StopBuild = 0x0108;
        public const int CommandId_GlobalSettings = 0x0109;
        public const int CommandId_SolutionSettings = 0x010A;
        public const int CommandId_About = 0x010B;

        public static readonly Guid CommandSet = new Guid("21ed6ae9-d3ad-4002-bc33-c339b7cf3eeb");	// Has to match the GUID in the .vsct file!

        private readonly AsyncPackage _package;
        private readonly DTE2 _application;
        private readonly OleMenuCommandService _commandService;

        private CommandManager _commandManager;
        private OutputPane _outputPane;
        private Builder _builder;

        private IDictionary<int, ICommand> _commands = new Dictionary<int, ICommand>();

        public static CommandHandler Instance { get; private set; }
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider { get { return _package; } }

        #region ICommandRegistrar implementation

        public const string VSIXCommandPrefix = "RudeBuild.";

        public EnvDTE.Command GetCommand(DTE2 application, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Commands2 vsCommands = (Commands2)application.Commands;
            var vsCommand = from Command command in vsCommands
                            where command.Name == VSIXCommandPrefix + name
                            select command;
            return vsCommand.FirstOrDefault();
        }

        public EnvDTE.Command RegisterCommand(DTE2 application, int id, string name, string caption, string toolTip, string icon, ICommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_commands.ContainsKey(id))
            {
                OleMenuCommand menuItem = new OleMenuCommand(OnExecuteCommand, new CommandID(CommandSet, id));
                menuItem.BeforeQueryStatus += new EventHandler(OnBeforeQueryStatus);
                _commandService.AddCommand(menuItem);

                _commands.Add(id, command);
            }

            return GetCommand(application, name);
        }

        #endregion

        private void RegisterCommands()
        {
            _commandManager.RegisterCommand(CommandId_BuildSolution, "BuildSolution", "&Build Solution", "RudeBuild: Build Solution", "3", new BuildSolutionCommand(_builder, BuildCommandBase.Mode.Build));
            _commandManager.RegisterCommand(CommandId_RebuildSolution, "RebuildSolution", "&Rebuild Solution", "RudeBuild: Rebuild Solution", null, new BuildSolutionCommand(_builder, BuildCommandBase.Mode.Rebuild));
            _commandManager.RegisterCommand(CommandId_CleanSolution, "CleanSolution", "&Clean Solution", "RudeBuild: Clean Solution", null, new BuildSolutionCommand(_builder, BuildCommandBase.Mode.Clean));
            _commandManager.RegisterCommand(CommandId_CleanCache, "CleanCache", "C&lean Cache", "RudeBuild: Clean RudeBuild Solution Cache", null, new CleanCacheCommand(_builder));
            _commandManager.RegisterCommand(CommandId_BuildProject, "BuildProject", "B&uild Project", "RudeBuild: Build Project", "2", new BuildProjectCommand(_builder, BuildCommandBase.Mode.Build));
            _commandManager.RegisterCommand(CommandId_RebuildProject, "RebuildProject", "R&ebuild Project", "RudeBuild: Rebuild Project", null, new BuildProjectCommand(_builder, BuildCommandBase.Mode.Rebuild));
            _commandManager.RegisterCommand(CommandId_CleanProject, "CleanProject", "Clea&n Project", "RudeBuild: Clean Project", null, new BuildProjectCommand(_builder, BuildCommandBase.Mode.Clean));
            _commandManager.RegisterCommand(CommandId_StopBuild, "StopBuild", "&Stop Build", "RudeBuild: Stop Build", "5", new StopBuildCommand(_builder));
            _commandManager.RegisterCommand(CommandId_GlobalSettings, "GlobalSettings", "&Global Settings...", "Opens the RudeBuild Global Settings Dialog", "4", new GlobalSettingsCommand(_builder));
            _commandManager.RegisterCommand(CommandId_SolutionSettings, "SolutionSettings", "S&olution Settings...", "Opens the RudeBuild Solution Settings Dialog", "4", new SolutionSettingsCommand(_builder, _outputPane));
            _commandManager.RegisterCommand(CommandId_About, "About", "&About", "About RudeBuild", null, new AboutCommand());
        }

        private int GetPopupMenuBarInsertIndex(CommandBar parentCommandBar)
        {
            CommandBarControl commandBarControl = _commandManager.FindCommandBarControlByCaption(parentCommandBar, "IncrediBuild");
            if (null != commandBarControl)
                return commandBarControl.Index;

            if (parentCommandBar.Name == "Project")
            {
                commandBarControl = _commandManager.FindCommandBarControlByCaption(parentCommandBar, "Clean");
                if (null != commandBarControl)
                    return commandBarControl.Index + 1;
            }
            else if (parentCommandBar.Name == "Solution")
            {
                commandBarControl = _commandManager.FindCommandBarControlByCaption(parentCommandBar, "Clean Solution");
                if (null != commandBarControl)
                    return commandBarControl.Index + 1;
            }

            return 5;
        }

        private void AddProjectRightClickMenuToUI()
        {
            IList<CommandBar> parentCommandBars = _commandManager.FindCommandBars("Project");
            foreach (CommandBar parentCommandBar in parentCommandBars)
            {
                CommandBar commandBar = _commandManager.AddPopupCommandBar(parentCommandBar, "RudeBuild", "R&udeBuild", GetPopupMenuBarInsertIndex(parentCommandBar), beginGroup: true);
                int insertIndex = 1;
                _commandManager.AddCommandToCommandBar(commandBar, "BuildProject", insertIndex++);
                _commandManager.AddCommandToCommandBar(commandBar, "RebuildProject", insertIndex++);
                _commandManager.AddCommandToCommandBar(commandBar, "CleanProject", insertIndex++);
            }
        }

        private void AddSolutionRightClickMenuToUI()
        {
            IList<CommandBar> parentCommandBars = _commandManager.FindCommandBars("Solution");
            foreach (CommandBar parentCommandBar in parentCommandBars)
            {
                CommandBar commandBar = _commandManager.AddPopupCommandBar(parentCommandBar, "RudeBuild", "R&udeBuild", GetPopupMenuBarInsertIndex(parentCommandBar), beginGroup: true);
                int insertIndex = 1;
                _commandManager.AddCommandToCommandBar(commandBar, "BuildSolution", insertIndex++);
                _commandManager.AddCommandToCommandBar(commandBar, "RebuildSolution", insertIndex++);
                _commandManager.AddCommandToCommandBar(commandBar, "CleanSolution", insertIndex++);
            }
        }

        private void AddToolbarToUI()
        {
            CommandBar commandBar = _commandManager.AddCommandBar("RudeBuild", MsoBarPosition.msoBarTop);
            int insertIndex = 1;
            _commandManager.AddCommandToCommandBar(commandBar, "BuildProject", insertIndex++, style: MsoButtonStyle.msoButtonIcon);
            _commandManager.AddCommandToCommandBar(commandBar, "BuildSolution", insertIndex++, style: MsoButtonStyle.msoButtonIcon);
            _commandManager.AddCommandToCommandBar(commandBar, "StopBuild", insertIndex++, beginGroup: true, style: MsoButtonStyle.msoButtonIcon);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandHandler"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private CommandHandler(AsyncPackage package, OleMenuCommandService commandService, DTE2 application)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            _application = application ?? throw new ArgumentNullException(nameof(application));

            try
            {
                _commandManager = new CommandManager(_application, this);
                _outputPane = new OutputPane(_application, "RudeBuild");
                _builder = new Builder(_outputPane);

                RegisterCommands();
                AddProjectRightClickMenuToUI();
                AddSolutionRightClickMenuToUI();
                AddToolbarToUI();
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(_package, "RudeBuild initialization error!\n" + ex.Message, "RudeBuild",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in Command1's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            DTE2 application = await package.GetServiceAsync(typeof(DTE)) as DTE2;

            Instance = new CommandHandler(package, commandService, application);
        }

        private void OnExecuteCommand(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = sender as MenuCommand;
            if (null == menuCommand)
                return;

            try
            {
                ICommand command;
                if (_commands.TryGetValue(menuCommand.CommandID.ID, out command))
                {
                    command.Execute(_commandManager);
                }
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(_package, "An internal RudeBuild exception occurred!\n" + ex.Message, "RudeBuild",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = sender as MenuCommand;
            if (null == menuCommand)
                return;

            try
            {
                ICommand command;
                if (_commands.TryGetValue(menuCommand.CommandID.ID, out command))
                {
                    bool isEnabled = command.IsEnabled(_commandManager);
                    if (isEnabled != menuCommand.Enabled)
                    {
                        menuCommand.Enabled = isEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(_package, "An internal RudeBuild exception occurred!\n" + ex.Message, "RudeBuild",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
