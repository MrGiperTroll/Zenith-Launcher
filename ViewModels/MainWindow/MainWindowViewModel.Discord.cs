using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Theming;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;
using ZenithLauncher.Views;

namespace ZenithLauncher.ViewModels;


public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _discordRpcEnabled = true;



    partial void OnDiscordRpcEnabledChanged(bool value)

    {

        if (!value)

        {

            Services.DiscordPresenceService.Shutdown();

            LauncherLog.Info("Discord RPC: disabled via settings toggle.");

        }

        else

        {

            Services.DiscordPresenceService.Initialize(true);

            Services.DiscordPresenceService.SetBrowsingProfiles();

            LauncherLog.Info("Discord RPC: enabled via settings toggle.");

        }

    }




}
