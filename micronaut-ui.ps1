Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms, System.Drawing

$csDir = Join-Path $PSScriptRoot "src\NeuralGrammar.Core"
if (-not (Test-Path $csDir)) { $csDir = Join-Path (Get-Location) "src\NeuralGrammar.Core" }
if (Test-Path $csDir) {
    $files = Get-ChildItem $csDir -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" } | ForEach-Object { $_.FullName }
    if ($files) { Add-Type -Path $files -ErrorAction SilentlyContinue | Out-Null }

}
# Runtime-extension module: @flux, worker dispatch, validation helpers.
$runtimeModule = Join-Path $PSScriptRoot "scripts\NNCK-Runtime.psm1"
if (Test-Path $runtimeModule) { Import-Module $runtimeModule -Force -ErrorAction SilentlyContinue }

# ============================================================================
# Defensive JSON parse: never throws. Returns $null on empty/invalid input, and
# retries by extracting the first {...} / [...] out of surrounding prose (model replies).
# ============================================================================
function ConvertFrom-JsonSafe {
    param([Parameter(ValueFromPipeline)] $InputObject, [int]$Depth = 64)
    process {
        $s = [string]$InputObject
        if ([string]::IsNullOrWhiteSpace($s)) { return $null }
        try { return ($s | ConvertFrom-Json -Depth $Depth -ErrorAction Stop) } catch { }
        # retry: pull the JSON object/array out of any surrounding text
        $o = $s.IndexOf('{'); $c = $s.LastIndexOf('}'); $ob = $s.IndexOf('['); $cb = $s.LastIndexOf(']')
        $sub = if ($o -ge 0 -and $c -gt $o) { $s.Substring($o, $c - $o + 1) }
               elseif ($ob -ge 0 -and $cb -gt $ob) { $s.Substring($ob, $cb - $ob + 1) }
               else { $null }
        if ($sub) { try { return ($sub | ConvertFrom-Json -Depth $Depth -ErrorAction Stop) } catch { } }
        return $null
    }
}

# ============================================================================
# Theme system
# ============================================================================
$script:ThemeColors = $null
function Get-Theme {
    if ($script:ThemeColors) { return $script:ThemeColors }
    $themePath = Join-Path $PSScriptRoot "schemas\themes\dark.json"
    if (Test-Path $themePath) {
        try {
            $raw = Get-Content $themePath -Raw | ConvertFrom-JsonSafe
            $script:ThemeColors = [PSCustomObject]$raw.colors
            return $script:ThemeColors
        } catch { Update-Splash $script:SplashCtx "theme: load error" "$($_.Exception.Message)" }
    }
    # Fallback hardcoded palette
    $script:ThemeColors = [PSCustomObject]@{
        window = '#0d1117'; panel = '#161b22'; border = '#30363d'
        input = '#0d1117'; input_text = '#e2e8f0'; input_caret = '#58a6ff'
        text = '#e2e8f0'; text_muted = '#8b949e'; text_dim = '#585e6b'
        accent = '#58a6ff'; accent_bg = '#1f6feb'; success = '#3fb950'
        success_bg = '#238636'; warning = '#d29922'; error = '#f85149'
        error_bg = '#da3633'; selection = '#1f6feb'; selection_text = '#ffffff'
        hover = '#21262d'; tab_bg = '#161b22'; tab_active = '#0d1117'
        tab_text = '#e2e8f0'; tab_hover = '#21262d'
        transparent = 'Transparent'
    }
    return $script:ThemeColors
}

function Show-ThemedDialog {
    param($dlg)
    if (-not $dlg) { return }
    Set-ControlTheme $dlg
    $null = $dlg.ShowDialog()
}


function Set-ControlTheme {
    param($control)
    if (-not $control) { return }
    $t = Get-Theme
    try {
        $typeName = $control.GetType().Name
        switch ($typeName) {
            'TextBox' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.input }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.input_text }
                $control.CaretBrush = $t.input_caret
            }
            'PasswordBox' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.input }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.input_text }
            }
            'ComboBox' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.input }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.text }
                if ($control.BorderBrush.ToString() -eq '#FF828790') { $control.BorderBrush = $t.border }
                # Try to style the internal toggle button border
                try { $controlBorder = [Windows.Media.SolidColorBrush]::new(([Windows.Media.ColorConverter]::ConvertFromString($t.border))); $control.BorderBrush = $controlBorder } catch {}
            }
            'ListBox' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.panel }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.text }
            }
            'TabControl' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.panel }
            }
            'TabItem' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.tab_bg }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.tab_text }
            }
            'Border' {
                if ($control.Background -and $control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.panel }
            }
            'ScrollViewer' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.window }
            }
            'Button' {
                if ($control.Background.ToString() -eq '#FFDDDDDD') { $control.Background = $t.panel }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.text }
            }
            'Label' {
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.text }
            }
            'ComboBoxItem' {
                if ($control.Background.ToString() -eq '#FFFFFFFF') { $control.Background = $t.input }
                if ($control.Foreground.ToString() -eq '#FF000000') { $control.Foreground = $t.text }
            }
        }
    } catch {}
    # Recurse into children
    if ($control.HasLogicalChildren -or $control.Children -or $control.Content) {
        try {
            foreach ($child in $control.LogicalChildren) { Set-ControlTheme $child }
        } catch {}
        try {
            if ($control.Children) { foreach ($child in $control.Children) { Set-ControlTheme $child } }
        } catch {}
        try {
            if ($control.Content -and ($control.Content -is [System.Windows.UIElement])) { Set-ControlTheme $control.Content }
        } catch {}
    }
}



function Close-Splash($ctx) {
    if (-not $ctx -or -not $ctx.Window) { return }
    $elapsed = if ($ctx.StartTime) { [DateTime]::UtcNow - $ctx.StartTime.ToUniversalTime() } else { [TimeSpan]::Zero }
    $remaining = [TimeSpan]::FromSeconds(30) - $elapsed
    if ($remaining.TotalSeconds -gt 0) {
        $frame = New-Object Windows.Threading.DispatcherFrame
        $timer = New-Object Windows.Threading.DispatcherTimer
        $timer.Interval = $remaining
        $timer.Add_Tick({ $frame.Continue = $false })
        $timer.Start()
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        $timer.Stop()
    }
    try { $ctx.Window.Dispatcher.Invoke([action]{ $ctx.Window.Close() }, "Normal") } catch {}
}

function Update-Splash($ctx, $line1, $line2) {
    # HTML handles its own boot animation -- this is a no-op
    # Boot messages from PowerShell are still useful for debugging
    if ($env:DEBUG_SPLASH) { Write-Host "$line1 $line2" }
}

# PSScriptAnalyzer + AST diagnostics at startup
try {
    $analyzerPath = Join-Path $PSScriptRoot ".PSScriptAnalyzerSettings.psd1"
    if (Test-Path $analyzerPath) {
        $analyzerSettings = Import-PowerShellDataFile $analyzerPath
        if (Get-Command Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue) {
            $issues = Invoke-ScriptAnalyzer -Path $PSCommandPath -Settings $analyzerPath -ErrorAction SilentlyContinue
            if ($issues) {
                $errCount = ($issues | Where-Object { $_.Severity -eq 'Error' }).Count
                $warnCount = ($issues | Where-Object { $_.Severity -eq 'Warning' }).Count
                Update-Splash $script:SplashCtx "analyzer: $errCount errors" "$warnCount warnings"
            } else { Update-Splash $script:SplashCtx "analyzer" "clean" }
        } else { Update-Splash $script:SplashCtx "analyzer" "module not installed" }
    }
    # Native AST diagnostics
    $astTokens = $null; $astErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$astTokens, [ref]$astErrors)
    if ($ast) {
        $funcCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)).Count
        $varCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.VariableExpressionAst] }, $true)).Count
        $paramCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ParameterAst] }, $true)).Count
        $classCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.TypeDefinitionAst] }, $true)).Count
        $ifCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.IfStatementAst] }, $true)).Count
        $loopCount = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ForEachStatementAst] -or $args[0] -is [System.Management.Automation.Language.WhileStatementAst] -or $args[0] -is [System.Management.Automation.Language.DoWhileStatementAst] -or $args[0] -is [System.Management.Automation.Language.ForStatementAst] }, $true)).Count
        $tokenCount = $astTokens.Count
        $errorCount = if ($astErrors) { $astErrors.Count } else { 0 }
        Update-Splash $script:SplashCtx "AST: $funcCount functions, $varCount vars" "$ifCount ifs, $loopCount loops, $tokenCount tokens"
    }
} catch { Write-Host "ast/diag: $($_.Exception.Message)" }


$script:Root = $PSScriptRoot
$script:ActiveModel = "lfm-2.5-1.2b"

# ============================================================================
# Tool Registry — native executables available to all micronauts and models
# ============================================================================
$script:ToolRegistry = @{
    'micronaut_coder'   = @{ Path = Join-Path $script:Root 'bin\micronaut_coder.exe';   Desc = 'Code generation/compilation';      Tags = @('code','compile','generate','csharp') }
    'cpp_runtime'       = @{ Path = Join-Path $script:Root 'bin\micronaut_cpp_runtime.exe'; Desc = 'Native C++ runtime for micronaut execution'; Tags = @('native','runtime','cpp','execute') }
    'multi_format'      = @{ Path = Join-Path $script:Root 'bin\multi_format_executor.exe'; Desc = 'Multi-format executor (.kuhul/.kprog/JSON/XML)'; Tags = @('format','execute','parse','convert') }
    'native_glyph'      = @{ Path = Join-Path $script:Root 'bin\native_glyph_engine.exe'; Desc = 'Native glyph rendering/execution'; Tags = @('glyph','render','native','opcode') }
    'jsonl_executor'    = @{ Path = Join-Path $script:Root 'bin\jsonl_executor.exe';    Desc = 'JSONL batch processor for training/inference'; Tags = @('jsonl','batch','training','inference') }
    'micronaut_factory' = @{ Path = Join-Path $script:Root 'bin\micronaut_factory.exe'; Desc = 'Micronaut factory: genesis, bigram merge, mutation tracking'; Tags = @('genesis','create','merge','evolve','factory') }
    'web_search'        = @{ Path = 'builtin'; Desc = 'Web search capability (built-in)'; Tags = @('search','web','research','fetch') }
    'model_inference'   = @{ Path = 'builtin'; Desc = 'Model inference via API';         Tags = @('model','inference','chat','llm') }
    'adam_router'       = @{ Path = 'http://localhost:3167'; Desc = 'ADAM: bigram/trigram prompt router to reasoning/code/math experts'; Tags = @('routing','adaptive','bigram','expert','deterministic') }
}

function Invoke-Tool($toolName, $args, $timeoutMs = 30000) {
    if (-not $script:ToolRegistry.ContainsKey($toolName)) {
        Write-Console "tool: $toolName not found" "Error"
        return $null
    }
    $tool = $script:ToolRegistry[$toolName]
    if ($tool.Path -eq 'builtin') { return $null }
    if (-not (Test-Path $tool.Path)) {
        Write-Console "tool: $toolName missing at $($tool.Path)" "Error"
        return $null
    }
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $tool.Path
        $psi.Arguments = $args
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        if (-not $proc.WaitForExit($timeoutMs)) { $proc.Kill(); return @{ error = "timeout" } }
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        return @{ exitCode = $proc.ExitCode; stdout = $stdout; stderr = $stderr; tool = $toolName }
    } catch {
        Write-Console "tool: $toolName error $($_.Exception.Message)" "Error"
        return @{ error = $_.Exception.Message }
    }
}

function Get-ToolsByTag($tag) {
    $matches = @()
    foreach ($key in $script:ToolRegistry.Keys) {
        if ($script:ToolRegistry[$key].Tags -contains $tag) { $matches += $key }
    }
    return $matches
}

# tools registered (see ToolRegistry)

$script:Conversation = @()
$script:Attachments = @()
$script:MaxTk = 2048
$script:Endpoint = "http://127.0.0.1:1235"
$script:DataDir = Join-Path $script:Root ".learning"
$script:MicroDir = Join-Path $script:DataDir "micronauts"
$script:ChatDir = Join-Path $script:DataDir "chats"
$script:FluxStore = $null
$script:FluxSessionId = "default"
$script:CurrentUser = $null
$script:CurrentSession = $null
$script:XCFERuntime = $null
$script:MicronautRegister = $null
$script:MicronautManager = $null
$script:TensorEngine = $null
$script:Console = $null
$script:GoogleOAuth = $null
$script:LastShamanHtml = $null
$script:TickCounter = 0
$script:EventRegister = @()
$script:ExecutionTraces = @{}
$script:TopicConfidence = @{}   # topic -> provisional confidence carried across turns
$script:TopicLastResponse = @{} # topic -> last assistant response for contradiction detection
$script:LastMicronautId = $null # last micronaut touched in this session

# @flux persistence: initialize lineage store for the default session and load any prior traces.
try {
    $script:FluxStore = [NeuralGrammar.Core.Flux.FluxTraceStore]::new($script:DataDir)
    $script:FluxStore.SessionId = $script:FluxSessionId
    $fluxLoaded = $script:FluxStore.LoadSessionRawJson()
    foreach ($kv in $fluxLoaded.GetEnumerator()) { $script:ExecutionTraces[$kv.Key] = $kv.Value | ConvertFrom-JsonSafe }
} catch { }

# ============================================================================
# Splash Screen & Utility
# ============================================================================
function Add-Error($msg) {
    try { Add-ConsoleEntry "ERROR: $msg" "Error" } catch { }
}

function Show-SplashScreen {
    $splash = New-Object Windows.Window
    $splash.Title = "Neural Grammar Runtime"
    $splash.Width = 620; $splash.Height = 500
    $splash.WindowStartupLocation = "CenterScreen"
    $splash.Background = '#0d1117'; $splash.Foreground = '#e2e8f0'
    $splash.FontFamily = "Consolas"; $splash.FontSize = 17
    $splash.ResizeMode = "NoResize"; $splash.WindowStyle = "None"
    $splash.Topmost = $true
    $splash.WindowStartupLocation = "CenterScreen"

    $g = [Windows.Controls.Grid]::new(); $g.Margin = '0'
    $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition))
    $g.RowDefinitions[0].Height = [Windows.GridLength]::new(500)

    # Full SVG-3D boot logo with built-in console
    $logoBrowser = New-Object System.Windows.Controls.WebBrowser
    $logoHtmlPath = Join-Path $PSScriptRoot "schemas/themes/kuhul-logo.html"
    if (Test-Path $logoHtmlPath) {
        $logoHtml = Get-Content $logoHtmlPath -Raw
        $logoBrowser.NavigateToString($logoHtml)
    }
    $logoBrowser.MaxHeight = 500
    [Windows.Controls.Grid]::SetRow($logoBrowser, 0); $g.Children.Add($logoBrowser)

    $splash.Content = $g
    $splash.Show()
    $splash.UpdateLayout()
    # Pump 200ms of dispatcher frames so the splash renders before init continues
    $frame = New-Object Windows.Threading.DispatcherFrame
    $timer = New-Object Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromMilliseconds(200)
    $timer.Add_Tick({ $frame.Continue = $false })
    $timer.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
    $timer.Stop()
    $splashStart = Get-Date
    return @{ Window = $splash; Console = $null; StartTime = $splashStart }
}

function Start-PyScript {
    param($scriptInfo)
    $fullPath = Join-Path $PSScriptRoot $scriptInfo.file
    if (-not (Test-Path $fullPath)) { $fullPath = Join-Path (Get-Location) $scriptInfo.file }
    if (-not (Test-Path $fullPath)) { Write-Host "boot: $($scriptInfo.name).py not found"; return $null }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "py.exe"
    $psi.Arguments = "`"$fullPath`" $($scriptInfo.args)"
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $script:PyProcesses += $proc
        Update-Splash $script:SplashCtx "$($scriptInfo.name) started" "PID $($proc.Id)"
        Start-Sleep -Milliseconds 1500
        return $proc
    } catch {
        Update-Splash $script:SplashCtx "$($scriptInfo.name) failed" "$($_.Exception.Message)"
        return $null
    }
}

function Stop-PyProcesses {
    foreach ($proc in $script:PyProcesses) {
        if ($proc -and -not $proc.HasExited) {
            try { $proc.Kill(); $proc.WaitForExit(3000) } catch { }
        }
    }
    $script:PyProcesses = @()
}

# Register cleanup on exit
Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { Stop-PyProcesses } | Out-Null

# Start both Python scripts
Update-Splash $script:SplashCtx "Starting servers..." "model_server + xcfe_router"
foreach ($s in $script:PyScripts) { $null = Start-PyScript $s }

# Verify ports are responding
Start-Sleep -Seconds 2
foreach ($s in $script:PyScripts) {
    try {
        $wc = New-Object System.Net.WebClient
        $wc.DownloadString("http://127.0.0.1:$($s.port)/health")
        Update-Splash $script:SplashCtx "$($s.name) ready" "port $($s.port)"
    } catch {
        Update-Splash $script:SplashCtx "$($s.name) starting" "port $($s.port)"
    }
}
[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Micronaut Chat" Height="800" Width="1200"
        WindowStartupLocation="CenterScreen"
        Background="#0d1117" Foreground="#e2e8f0" FontFamily="Consolas" FontSize="13">
  <Window.Resources>
    <Style TargetType="ComboBoxItem">
      <Setter Property="Background" Value="#0d1117"/>
      <Setter Property="Foreground" Value="#000"/>
      <Setter Property="BorderBrush" Value="#30363d"/>
      <Style.Resources>
        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#1f6feb"/>
        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#e2e8f0"/>
      </Style.Resources>
    </Style>
  </Window.Resources>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="36"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <Border Grid.Row="0" Background="#161b22" BorderBrush="#30363d" BorderThickness="0,0,0,1" Padding="12,0">
      <DockPanel>
        <TextBlock DockPanel.Dock="Left" Text=" Micronaut Chat" FontSize="14" Foreground="#58a6ff" VerticalAlignment="Center"/>
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
          <Button x:Name="BtnUser" Content="[?]" Width="32" Height="22" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Cursor="Hand" FontSize="10" ToolTip="Sign in"/>
          <Button x:Name="BtnWizard" Content="[W]" Width="28" Height="22" FontSize="10" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Cursor="Hand" Margin="2,0,0,0" ToolTip="K'UHUL Shaman (create .kuhul programs)"/>
          <TextBlock x:Name="UserNameText" Text="" FontSize="11" Foreground="#8b949e" VerticalAlignment="Center" Margin="4,0,8,0"/>
          <ComboBox x:Name="ModelSelector" Width="140" Height="22" FontSize="10" Background="#0d1117" Foreground="#e2e8f0" BorderBrush="#30363d" Margin="4,0,0,0" VerticalAlignment="Center" ToolTip="Select active model"/>
          <Button x:Name="BtnClose" Content="X" Width="28" Height="22" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Cursor="Hand" FontSize="12"/>
        </StackPanel>
      </DockPanel>
    </Border>
    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column="0" x:Name="SidebarPanel" Width="220" Background="#161b22" BorderBrush="#30363d" BorderThickness="0,0,1,0" Visibility="Collapsed">
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="*"/><RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>
          <StackPanel Grid.Row="0">
            <Border Background="#0d1117" BorderBrush="#30363d" BorderThickness="0,0,0,1" Padding="8,6">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="CHATS" FontSize="10" Foreground="#58a6ff" FontWeight="Bold" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <Button x:Name="BtnNewChat" Content="+" Width="24" Height="20" FontSize="12" Background="#1f6feb" Foreground="White" BorderThickness="0" Padding="0" Cursor="Hand"/>
                <Button x:Name="BtnToggleSidebar" Content="&lt;" Width="20" Height="20" FontSize="10" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Padding="0" Cursor="Hand" Margin="4,0,0,0"/>
              </StackPanel>
            </Border>
            <ScrollViewer x:Name="SidebarScroll" VerticalScrollBarVisibility="Auto" Height="540">
              <StackPanel x:Name="ChatListPanel" Margin="4"/>
            </ScrollViewer>
          </StackPanel>
          <Border Grid.Row="1" x:Name="ProfileBox" Background="#0d1117" BorderBrush="#30363d" BorderThickness="0,1,0,0" Padding="10,7" Cursor="Hand">
            <StackPanel Orientation="Horizontal">
              <Border x:Name="ProfileAvatar" Width="24" Height="24" CornerRadius="12" Background="#2d2d44" VerticalAlignment="Center" Margin="0,0,8,0"><TextBlock x:Name="ProfileIcon" Text="?" FontSize="11" Foreground="#e2e8f0" HorizontalAlignment="Center" VerticalAlignment="Center" FontWeight="Bold"/></Border>
              <StackPanel VerticalAlignment="Center">
                <TextBlock x:Name="ProfileName" Text="Not signed in" FontSize="11" Foreground="#e2e8f0"/>
                <TextBlock x:Name="ProfileStatus" Text="Click to sign in" FontSize="9" Foreground="#8b949e"/>
              </StackPanel>
            </StackPanel>
          </Border>
        </Grid>
      </Border>
      <Grid Grid.Column="1">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="200"/>
        </Grid.RowDefinitions>
        <Border Grid.Row="0" Background="#161b22" BorderBrush="#30363d" BorderThickness="0,0,0,1" Padding="8,4">
          <StackPanel Orientation="Horizontal">
            <Button x:Name="BtnShowSidebar" Content="[=]" Width="32" Height="22" FontSize="10" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Padding="0" Cursor="Hand"/>
            <TextBlock x:Name="ChatTitleBar" Text="  Micronaut Chat" FontSize="12" Foreground="#8b949e" VerticalAlignment="Center" Margin="8,0,0,0"/>
            <Button x:Name="BtnSvg" Content="[V]" Width="32" Height="22" FontSize="10" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Padding="0" Cursor="Hand" Margin="6,0,0,0" ToolTip="Phase visualizer"/>
            <Button x:Name="BtnInspector" Content="[I]" Width="28" Height="22" FontSize="10" Background="Transparent" Foreground="#8b949e" BorderThickness="0" Padding="0" Cursor="Hand" Margin="2,0,0,0" ToolTip="Runtime Inspector"/>
          </StackPanel>
        </Border>
        <ScrollViewer Grid.Row="1" x:Name="FeedScroll" VerticalScrollBarVisibility="Auto" Background="#0d1117">
          <StackPanel x:Name="Feed" Margin="12,6,12,6"/>
        </ScrollViewer>
        <TabControl Grid.Row="2" x:Name="ConsoleTabs" Background="#0d1117" BorderBrush="#30363d" BorderThickness="0,1,0,0" FontSize="11">
          <TabItem Header="Console" Background="#161b22" Foreground="#e2e8f0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Background="#0d1117">
              <StackPanel x:Name="ConsolePanel" Margin="6,4,6,4"/>
            </ScrollViewer>
          </TabItem>
          <TabItem Header="Runtime" Background="#161b22" Foreground="#e2e8f0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Background="#0d1117">
              <StackPanel x:Name="RuntimePanel" Margin="6,4,6,4"/>
            </ScrollViewer>
          </TabItem>
          <TabItem Header="Errors" Background="#161b22" Foreground="#e2e8f0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Background="#0d1117">
              <StackPanel x:Name="ErrorsPanel" Margin="6,4,6,4"/>
            </ScrollViewer>
          </TabItem>
          <TabItem Header="Inference" Background="#161b22" Foreground="#e2e8f0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Background="#0d1117">
              <StackPanel x:Name="InferencePanel" Margin="6,4,6,4"/>
            </ScrollViewer>
          </TabItem>
          <TabItem Header="Network" Background="#161b22" Foreground="#e2e8f0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Background="#0d1117">
              <StackPanel x:Name="NetworkPanel" Margin="6,4,6,4"/>
            </ScrollViewer>
          </TabItem>
        </TabControl>
      </Grid>
    </Grid>
    <Border Grid.Row="2" Background="#161b22" BorderBrush="#30363d" BorderThickness="0,1,0,0" Padding="10,6">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox Grid.Column="0" x:Name="InputBox" Background="#0d1117" Foreground="#e2e8f0" CaretBrush="#58a6ff"
                 BorderBrush="#30363d" BorderThickness="1" FontSize="13" Padding="10,6" Height="34" VerticalContentAlignment="Center"
                 TextWrapping="Wrap" AcceptsReturn="True"/>
        <Button Grid.Column="1" x:Name="AttachBtn" Content="+" Width="36" Height="34" Margin="4,0,0,0"
                Background="#21262d" Foreground="#8b949e" BorderThickness="0" FontSize="16" Cursor="Hand" ToolTip="Attach"/>
        <Button Grid.Column="2" x:Name="SendBtn" Content="Send" Background="#1f6feb" Foreground="White" BorderThickness="0"
                FontSize="12" FontWeight="Bold" Height="34" Width="64" Margin="4,0,0,0" Cursor="Hand"/>
      </Grid>
    </Border>
  </Grid>
</Window>
'@
$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [System.Windows.Markup.XamlReader]::Load($reader)
# Apply theme to all controls
Set-ControlTheme $window
$feed = $window.FindName('Feed'); $feedScroll = $window.FindName('FeedScroll')
# Set global system resource overrides -- controls dropdowns, popups, highlights, scrollbars
$t = Get-Theme
function Set-ThemeResource($key, $color) {
    try { $window.Resources[$key] = [Windows.Media.SolidColorBrush]::new(([Windows.Media.ColorConverter]::ConvertFromString($color))) } catch {}
}
Set-ThemeResource "{x:Static SystemColors.HighlightBrushKey}" $t.selection
Set-ThemeResource "{x:Static SystemColors.HighlightTextBrushKey}" $t.selection_text
Set-ThemeResource "{x:Static SystemColors.WindowBrushKey}" $t.input
Set-ThemeResource "{x:Static SystemColors.WindowTextBrushKey}" $t.input_text
Set-ThemeResource "{x:Static SystemColors.ControlBrushKey}" $t.panel
Set-ThemeResource "{x:Static SystemColors.ControlTextBrushKey}" $t.text
Set-ThemeResource "{x:Static SystemColors.InactiveSelectionHighlightBrushKey}" $t.hover
Set-ThemeResource "{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" $t.text
Set-ThemeResource "{x:Static SystemColors.GrayTextBrushKey}" $t.text_muted
Set-ThemeResource "{x:Static SystemColors.HotTrackBrushKey}" $t.accent
# ComboBoxItem and ListBoxItem default styles
$itemStyle = [Windows.Style]::new([Windows.Controls.ListBoxItem])
$itemStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::BackgroundProperty), $t.transparent))
$itemStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::ForegroundProperty), $t.text))
$itemTrigger = New-Object Windows.Trigger
$itemTrigger.Property = [Windows.Controls.ListBoxItem]::IsHighlightedProperty
$itemTrigger.Value = $true
$null = $itemTrigger.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::BackgroundProperty), $t.selection))
$null = $itemTrigger.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::ForegroundProperty), $t.selection_text))
$null = $itemStyle.Triggers.Add($itemTrigger)
$window.Resources["{x:Type ListBoxItem}"] = $itemStyle
# Same for ComboBoxItem
$cbiStyle = [Windows.Style]::new([Windows.Controls.ComboBoxItem])
$cbiStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::BackgroundProperty), $t.transparent))
$cbiStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::ForegroundProperty), $t.text))
$cbiTrigger = New-Object Windows.Trigger
$cbiTrigger.Property = [Windows.Controls.ComboBoxItem]::IsHighlightedProperty
$cbiTrigger.Value = $true
$null = $cbiTrigger.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::BackgroundProperty), $t.selection))
$null = $cbiTrigger.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::ForegroundProperty), $t.selection_text))
$null = $cbiStyle.Triggers.Add($cbiTrigger)
$window.Resources["{x:Type ComboBoxItem}"] = $cbiStyle

# Dark ComboBox ControlTemplate -- removes all system default brushes
$comboTemplateXaml = @'
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 TargetType="ComboBox">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <Border x:Name="Border" Grid.ColumnSpan="2" Background="#0d1117" BorderBrush="#30363d" BorderThickness="1" CornerRadius="3"/>
    <Border x:Name="SelectedBorder" Grid.Column="0" Background="Transparent" Margin="4,0,0,0" Padding="4,0">
      <ContentPresenter x:Name="ContentSite" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" VerticalAlignment="Center"/>
    </Border>
    <ToggleButton x:Name="ToggleButton" Grid.Column="1" Background="#161b22" BorderBrush="#30363d" BorderThickness="0" Width="22" Cursor="Hand" >
      <Path x:Name="Arrow" Data="M0,0 L5,5 L10,0" Stroke="#8b949e" StrokeThickness="1.5" Fill="Transparent" HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </ToggleButton>
    <Popup x:Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Slide">
      <Grid MinWidth="{TemplateBinding ActualWidth}" MaxHeight="300">
        <Border Background="#161b22" BorderBrush="#30363d" BorderThickness="1" CornerRadius="3">
          <ScrollViewer Background="Transparent">
            <ItemsPresenter x:Name="ItemsPresenter" KeyboardNavigation.DirectionalNavigation="Contained"/>
          </ScrollViewer>
        </Border>
      </Grid>
    </Popup>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
      <Setter TargetName="Border" Property="BorderBrush" Value="#58a6ff"/>
    </Trigger>
    <Trigger Property="IsKeyboardFocusWithin" Value="True">
      <Setter TargetName="Border" Property="BorderBrush" Value="#58a6ff"/>
    </Trigger>
    <Trigger Property="IsDropDownOpen" Value="True">
      <Setter TargetName="Border" Property="BorderBrush" Value="#58a6ff"/>
      <Setter TargetName="Arrow" Property="Stroke" Value="#58a6ff"/>
    </Trigger>
    <Trigger Property="IsEnabled" Value="False">
      <Setter TargetName="Border" Property="Background" Value="#0d1117"/>
      <Setter TargetName="Border" Property="BorderBrush" Value="#21262d"/>
      <Setter Property="Foreground" Value="#585e6b"/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>
'@
$comboTemplateReader = [System.Xml.XmlNodeReader]::new((New-Object System.Xml.XmlDocument | ForEach-Object { $_.LoadXml($comboTemplateXaml); $_ }))
$comboTemplate = [System.Windows.Markup.XamlReader]::Load($comboTemplateReader)

# Dark TabControl/TabItem template
$tabTemplateXaml = @'
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 TargetType="TabControl">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    <TabPanel x:Name="HeaderPanel" Grid.Row="0" Panel.ZIndex="1" IsItemsHost="True" Background="#161b22" Margin="0"/>
    <Border x:Name="ContentBorder" Grid.Row="1" Background="#0d1117" BorderBrush="#30363d" BorderThickness="1" CornerRadius="0,0,3,3">
      <ContentPresenter x:Name="ContentSite" Content="{TemplateBinding SelectedContent}" ContentTemplate="{TemplateBinding SelectedContentTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" Margin="4"/>
    </Border>
  </Grid>
</ControlTemplate>
'@
$tabTemplateReader = [System.Xml.XmlNodeReader]::new((New-Object System.Xml.XmlDocument | ForEach-Object { $_.LoadXml($tabTemplateXaml); $_ }))
$tabTemplate = [System.Windows.Markup.XamlReader]::Load($tabTemplateReader)

# Dark TabItem template
$tabItemTemplateXaml = @'
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 TargetType="TabItem">
  <Border x:Name="Border" Background="#161b22" BorderBrush="#30363d" BorderThickness="0,0,0,0" Padding="10,4,10,4" Cursor="Hand">
    <ContentPresenter x:Name="Content" Content="{TemplateBinding Header}" ContentTemplate="{TemplateBinding HeaderTemplate}" ContentStringFormat="{TemplateBinding HeaderStringFormat}" VerticalAlignment="Center" HorizontalAlignment="Center"/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
      <Setter TargetName="Border" Property="Background" Value="#1f2937"/>
    </Trigger>
    <Trigger Property="IsSelected" Value="True">
      <Setter TargetName="Border" Property="Background" Value="#0d1117"/>
      <Setter TargetName="Border" Property="BorderBrush" Value="#30363d"/>
      <Setter TargetName="Border" Property="BorderThickness" Value="1,1,1,0"/>
      <Setter Property="Foreground" Value="#e2e8f0"/>
    </Trigger>
    <Trigger Property="IsEnabled" Value="False">
      <Setter TargetName="Border" Property="Background" Value="#161b22"/>
      <Setter Property="Foreground" Value="#585e6b"/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>
'@
$tabItemTemplateReader = [System.Xml.XmlNodeReader]::new((New-Object System.Xml.XmlDocument | ForEach-Object { $_.LoadXml($tabItemTemplateXaml); $_ }))
$tabItemTemplate = [System.Windows.Markup.XamlReader]::Load($tabItemTemplateReader)

# Apply to all TabControls -- wrap templates in Style for implicit type resolution
$tabStyle = New-Object Windows.Style ([Windows.Controls.TabControl])
$null = $tabStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::TemplateProperty), $tabTemplate))
try { $consoleTabs.Template = $tabTemplate } catch {}
try {
    $tabItemStyle = New-Object Windows.Style ([Windows.Controls.TabItem])
    $null = $tabItemStyle.Setters.Add((New-Object Windows.Setter ([Windows.Controls.Control]::TemplateProperty), $tabItemTemplate))
    $consoleTabs.Resources.Add([Windows.Controls.TabItem], $tabItemStyle)
} catch {}
Set-ControlTheme $consoleTabs




$consolePanel = $window.FindName('ConsolePanel')
$runtimePanel = $window.FindName('RuntimePanel')
$errorsPanel = $window.FindName('ErrorsPanel')
$inferencePanel = $window.FindName('InferencePanel')
$networkPanel = $window.FindName('NetworkPanel')
$consoleTabs = $window.FindName('ConsoleTabs')
$script:Console = [NeuralGrammar.Core.MicronautConsole]::new(100)
$inputBox = $window.FindName('InputBox'); $sendBtn = $window.FindName('SendBtn')
$attachBtn = $window.FindName('AttachBtn')
$btnClose = $window.FindName('BtnClose')
$modelSelector = $window.FindName('ModelSelector')
# Apply dark ControlTemplate
if ($modelSelector) { $modelSelector.Template = $comboTemplate }
$models = @("lfm-2.5-1.2b", "gpt-oss-20b", "qwen-0.5b", "gpt2", "deepseek-chat", "deepseek-reasoner", "gemma4:cloud", "deepseek-v4-pro:cloud")
foreach ($m in $models) { $modelSelector.Items.Add($m) | Out-Null }
if ($script:ActiveModel -eq "gpt2") {
    $modelSelector.BorderBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(248,81,73))
    $modelSelector.BorderThickness = [Windows.Thickness]::new(2)
}

function New-Bubble($t, $bg, $fg, $left, $tick) {
    $b = [System.Windows.Controls.Border]::new()
    $b.CornerRadius = '12'; $b.Margin = '0,4,0,0'; $b.Padding = '14,10,14,10'; $b.MaxWidth = 720
    $b.Background = $bg; $b.HorizontalAlignment = if ($left) { 'Left' } else { 'Right' }
    $inner = [Windows.Controls.StackPanel]::new()
    $tx = [System.Windows.Controls.TextBox]::new(); $tx.Text = $t; $tx.TextWrapping = 'Wrap'; $tx.FontSize = 13
    $tx.Foreground = $fg; $tx.IsReadOnly = $true; $tx.Background = 'Transparent'; $tx.BorderThickness = '0'; $tx.Cursor = 'IBeam'
    $null = $inner.Children.Add($tx)
    if ($tick -ne $null) {
        $tickBar = [Windows.Controls.Border]::new()
        $tickBar.Background = '#1f6feb'; $tickBar.CornerRadius = '3'; $tickBar.Padding = '4,1,4,1'; $tickBar.Margin = '0,4,0,0'; $tickBar.Cursor = 'Hand'
        $tickBar.HorizontalAlignment = if ($left) { 'Left' } else { 'Right' }
        $tickBar.ToolTip = "Click to inspect execution state at tick $tick"
        $tl = New-Object Windows.Controls.TextBlock
        $tl.Text = " Tick#$tick "; $tl.FontSize = 8; $tl.Foreground = '#ffffff'
        $tickBar.Child = $tl
        $tickBar.Add_MouseDown({ Show-TickInspector $tick })
        $null = $inner.Children.Add($tickBar)
    }
    $b.Child = $inner
    return $b
}
function Add-User($t) {
    try { $window.Dispatcher.Invoke([action]{
        $b = New-Object Windows.Controls.Border; $b.CornerRadius='12'; $b.Margin='0,4,0,0'; $b.Padding='14,10,14,10'; $b.MaxWidth=720
        $b.Background='#1f6feb'; $b.HorizontalAlignment='Right'
        $tx = New-Object Windows.Controls.TextBox; $tx.Text=$t; $tx.TextWrapping='Wrap'; $tx.FontSize=13
        $tx.Foreground='#ffffff'; $tx.IsReadOnly=$true; $tx.Background='Transparent'; $tx.BorderThickness='0'; $tx.Cursor='IBeam'
        $b.Child=$tx; $feed.Children.Add($b); $feedScroll.ScrollToBottom()
    }, "Normal") } catch { }
}
function Add-AI($t, $meta = $null) {
    try { $window.Dispatcher.Invoke([action]{
        $outer = [System.Windows.Controls.StackPanel]::new()
        $outer.Margin = '0,4,0,0'; $outer.HorizontalAlignment = 'Left'
        $b = New-Object Windows.Controls.Border; $b.CornerRadius='12'; $b.Margin='0,0,0,0'; $b.Padding='14,10,14,10'; $b.MaxWidth=720
        $b.Background='#21262d'; $b.HorizontalAlignment='Left'
        $tx = New-Object Windows.Controls.TextBox; $tx.Text=$t; $tx.TextWrapping='Wrap'; $tx.FontSize=13
        $tx.Foreground='#e2e8f0'; $tx.IsReadOnly=$true; $tx.Background='Transparent'; $tx.BorderThickness='0'; $tx.Cursor='IBeam'
        $b.Child=$tx; $outer.Children.Add($b)
        if ($meta) {
            try {
                $badge = New-ChatMetadataBadge `
                    -Tick $meta.Tick `
                    -Model $meta.Model `
                    -Confidence $meta.Confidence `
                    -Brain $meta.Brain `
                    -Fold $meta.Fold `
                    -Intent $meta.Intent `
                    -MicronautActions $meta.MicronautActions `
                    -Sources $meta.Sources `
                    -Timestamp $meta.Timestamp
                if ($badge -is [System.Windows.Controls.TextBlock]) { $outer.Children.Add($badge) }
            } catch { }
        }
        $feed.Children.Add($outer); $feedScroll.ScrollToBottom()
    }, "Normal") } catch { }
}

function Add-ConsoleEntry($msg, $cat) {
    # Push to EventRegister (single source of truth)
    $script:TickCounter++
    $script:EventRegister += [PSCustomObject]@{
        Tick = $script:TickCounter
        Time = (Get-Date -Format 'HH:mm:ss.fff')
        Message = $msg
        Category = $cat
    }
    if ($script:EventRegister.Count -gt 1000) { $script:EventRegister = $script:EventRegister[-500..-1] }
    # Render to Console tab (full stream, unfiltered)
    if ($consolePanel) { try {
        $tb = [System.Windows.Controls.TextBlock]::new()
        $tb.Text = "  $msg"; $tb.FontSize = 9; $tb.Margin = '2,1,0,1'; $tb.ToolTip = $cat
        switch ($cat) {
            "Error"      { $tb.Foreground = '#f85149' }
            "ToolCall"   { $tb.Foreground = '#d2a8ff' }
            "ToolResult" { $tb.Foreground = '#7ee787' }
            "Phase"      { $tb.Foreground = '#58a6ff' }
            "Model"      { $tb.Foreground = '#f0883e' }
            "Assistant"  { $tb.Foreground = '#e2e8f0' }
            "Tick"       { $tb.Foreground = '#79c0ff' }
            "Kernel"     { $tb.Foreground = '#56d4dd' }
            "Network"    { $tb.Foreground = '#c9d1d9' }
            default      { $tb.Foreground = '#8b949e' }
        }
        $consolePanel.Children.Add($tb) | Out-Null
        while ($consolePanel.Children.Count -gt 150) { $consolePanel.Children.RemoveAt(0) }
    } catch {} }
}

function Write-Console($t, $cat) {
    if (-not $consolePanel -or -not $script:Console) { return }
    try {
        $enumVal = [NeuralGrammar.Core.MicronautConsole+EntryCategory]::$cat
        $null = $script:Console.Write($t, $enumVal)
    } catch {} finally {
        Add-ConsoleEntry $t $cat
    }
}

function Sync-ConsoleRegister {
    if (-not $script:Console -or -not $consolePanel) { return }
    $r = $script:Console.Register
    if (-not $r) { return }
    $script:Console.WriteRegister()
    Add-ConsoleEntry "Register: $($r.Count) nodes active" "Phase"
    foreach ($n in $r.All) {
        Add-ConsoleEntry "  $($n.Phase): $($n.Subject) [$($n.Capability)]" "System"
    }
}


function Add-Sys($t) { 
    $window.Dispatcher.Invoke([action]{ 
        $tb = [System.Windows.Controls.TextBlock]::new(); $tb.Text = "  $t"; $tb.FontSize = 11; $tb.Margin = '4,2,0,2'; $tb.Foreground = '#8b949e'
        $feed.Children.Add($tb); $feedScroll.ScrollToBottom()
    }) 
}

# Tone
$script:Greetings = @("hello","hi","hey","yo","sup","wassup","whats up","howdy","greetings","good morning","good evening","good afternoon","hey there","hiya","ello")
$script:Slang_Pop = @("gonna","wanna","gotta","aint","yall","dunno","kinda","sorta","cuz","bro","dude","nah","yeah","yep","nope","gimme","lemme","outta")
$script:Slang_Wo = @("buildin","makin","writin","codin","hackin","tinkerin")
$script:Slang_Yax = @("thinkin","wonderin","guessin","figurin")
function Get-Tone($t) {
    $l = $t.ToLower().Trim()
    if ($l -match '^(hello|hi|hey|yo|sup|wassup|howdy|greetings|hiya|ello)[\s!\.]*$') { return "greeting" }
    if ($l -match '^(hello|hi|hey|yo|sup)[,\s]') { return "greeting+question" }
    $slangCount = 0
    foreach ($word in $script:Slang_Pop + $script:Slang_Wo + $script:Slang_Yax) { if ($l -match "\b$word\b") { $slangCount++ } }
    $wordCount = ($l -split '\s+').Count
    if ($wordCount -le 3 -and $l -match '\b(what|who|when|where|why|how)\b') { return "casual+query" }
    if ($slangCount -gt 0 -or $wordCount -le 5) { return "casual" }
    return "formal"
}
function Get-Fold($t) {
    $l = $t.ToLower(); $tone = Get-Tone $t
    if ($tone -eq "greeting") { return "He" }
    if ($tone -match "casual") { return "He" }
    if ($l -match 'load|search|find|read|fetch|look|what|who|when|where') { return "Pop" }
    if ($l -match 'build|create|write|code|implement|define|construct|make') { return "Wo" }
    if ($l -match 'plan|predict|analyze|compare|evaluate|why|explain|design|tell me|what is') { return "Yax" }
    if ($l -match 'execute|run|transform|convert|apply|calculate|compute') { return "Sek" }
    if ($l -match 'review|reflect|improve|optimize|refactor|summarize|check') { return "Chen" }
    if ($l -match 'save|export|done|finish|complete|replay|store|archive') { return "Xul" }
    return "Sek"
}

# Micronauts
function Show-DarkInput($prompt, $title, $default) {
    $dlg = New-Object Windows.Window
    $dlg.Title = $title; $dlg.Width = 420; $dlg.Height = 180
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window
    $dlg.Background = '#0d1117'; $dlg.Foreground = '#e2e8f0'
    $dlg.FontFamily = "Consolas"; $dlg.FontSize = 12
    $g = New-Object Windows.Controls.Grid; $g.Margin = '16'; $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition))
    $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)); $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition))
    $lbl = New-Object Windows.Controls.TextBlock; $lbl.Text = $prompt; $lbl.FontSize = 12; $lbl.Foreground = '#e2e8f0'; $lbl.Margin = '0,0,0,8'
    [Windows.Controls.Grid]::SetRow($lbl,0); $g.Children.Add($lbl)
    $tb = New-Object Windows.Controls.TextBox; $tb.Text = $default; $tb.FontSize = 12
    $tb.Background = '#161b22'; $tb.Foreground = '#e2e8f0'; $tb.BorderBrush = '#30363d'; $tb.CaretBrush = '#58a6ff'
    $tb.BorderThickness = '1'; $tb.Padding = '8,4'; $tb.Height = 28
    [Windows.Controls.Grid]::SetRow($tb,1); $g.Children.Add($tb)
    $btnPanel = New-Object Windows.Controls.StackPanel; $btnPanel.Orientation = "Horizontal"; $btnPanel.HorizontalAlignment = "Right"; $btnPanel.Margin = '0,12,0,0'
    $ok = New-Object Windows.Controls.Button; $ok.Content = "OK"; $ok.Background = '#1f6feb'; $ok.Foreground = 'White'; $ok.Width = 70; $ok.Height = 28; $ok.FontWeight = "Bold"; $ok.Margin = '0,0,8,0'
    $ok.Add_Click({ $dlg.DialogResult = $true; $dlg.Close() })
    $cancel = New-Object Windows.Controls.Button; $cancel.Content = "Cancel"; $cancel.Background = '#21262d'; $cancel.Foreground = '#e2e8f0'; $cancel.Width = 70; $cancel.Height = 28
    $cancel.Add_Click({ $dlg.DialogResult = $false; $dlg.Close() })
    $btnPanel.Children.Add($ok); $btnPanel.Children.Add($cancel)
    [Windows.Controls.Grid]::SetRow($btnPanel,2); $g.Children.Add($btnPanel)
    $tb.Add_KeyDown({ if ($_.Key -eq "Enter") { $dlg.DialogResult = $true; $dlg.Close() } })
    $dlg.Content = $g
    if ($dlg.ShowDialog()) { return $tb.Text } else { return $null }
}

function Invoke-Shaman {
    Add-Sys "K'UHUL Shaman: let's build a compiled program."

    # Step 1: Pick template
    $templates = @("MathTeacher", "Researcher", "Coder", "SocraticTutor", "CreativeWriter", "Custom")
    Add-AI "**Available templates:**`n$($templates -join ', ')"
    $choice = Show-DarkInput "Template? ($($templates -join ', '))" "K'UHUL Shaman" "MathTeacher"
    if (-not $choice) { Add-Sys "Shaman: cancelled"; return }
    if ($templates -notcontains $choice) { Add-Error "Unknown template: $choice"; return }

    # Step 2: Name it
    $name = Show-DarkInput "Program name:" "K'UHUL Shaman" "My$choice"
    if (-not $name) { Add-Sys "Shaman: cancelled"; return }
    Add-Sys "Shaman: scaffolding $choice -> $name.kuhul"

    # Step 3: Compile
    try {
        $src = [NeuralGrammar.Core.MicronautWizard]::Scaffold(
            (Join-Path $script:Root "schemas\programs"), $choice, $name)
        Add-Sys "Shaman: source -> $src"

        $reg = if ($script:MicronautRegister) { $script:MicronautRegister } else { $null }
        $result = [NeuralGrammar.Core.MicronautWizard]::Compile($src, $reg)

        if ($result.Success) {
            Add-Sys "Shaman: compiled -> $($result.KprogPath)"
            Add-AI "**Program: $name**`n  Status: **SUCCESS**`n  Closed-loop: $($result.IsClosedLoop)`n  Nodes installed: $($result.InstalledNodeCount)"
            Sync-ConsoleRegister
        } else {
            Add-Error "Shaman: compilation failed`n$($result.Error)"
        }
    } catch {
        Add-Error "Shaman error: $($_.Exception.Message)"
    }
}


function Invoke-Advise($question) {
    Add-Sys "Advise: escalating to BOSS..."
    try {
        $ctx = $script:Conversation[-4..-1] | ForEach-Object { "$($_.role): $($_.content)" }
        $ctxStr = $ctx -join " | "
        $body = @{ messages = @(
            @{ role = "system"; content = "You are a senior advisor. Analyze the question, reason step by step, and provide a clear answer." }
            @{ role = "user"; content = "Context: $ctxStr`n`nQuestion: $question" }
        ); model = "gpt-oss-20b"; max_tokens = 1024; temperature = 0.3 } | ConvertTo-Json -Depth 4 -Compress
        $req = [System.Net.HttpWebRequest]::Create("$($script:Endpoint)/v1/chat/completions")
        $req.Method = "POST"; $req.ContentType = "application/json"; $req.Timeout = 120000
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body); $req.ContentLength = $bytes.Length
        $req.GetRequestStream().Write($bytes,0,$bytes.Length)
        $rs = $req.GetResponse().GetResponseStream()
        $rd = New-Object System.IO.StreamReader($rs); $json = $rd.ReadToEnd(); $rd.Close()
        $resp = $json | ConvertFrom-JsonSafe
        $advice = $resp.choices[0].message.content
        Write-Console "Advise: BOSS returned analysis" "Model"
        return "[Advisor] $advice"
    } catch { return "Advise unavailable: $($_.Exception.Message)" }
}

function Save-Micronaut($Topic, $Response, $Fold, [switch]$Refine) {
    if ([string]::IsNullOrEmpty($Response) -or $Response.Length -lt 20) { return }
    $words = ($Response -split '\s+' | Where-Object { $_.Length -gt 3 -and $_.Length -lt 25 }) | Select-Object -First 3
    $id = if ($words) { ($words[0..[Math]::Min(1, $words.Count-1)] -join '-').ToLower() -replace '[^a-z0-9-]','' } else { [System.Guid]::NewGuid().ToString("N").Substring(0,8) }
    if ($id.Length -lt 3) { $id = [System.Guid]::NewGuid().ToString("N").Substring(0,8) }
    $title = ($Response -split '\.')[0]; if ($title.Length -gt 80) { $title = $title.Substring(0,77) + "..." }
    $r = $Response.ToLower(); $detectedFold = $Fold
    if ($r -match 'calculate|result|value|answer is|equals|formula') { $detectedFold = "Sek" }
    elseif ($r -match 'search|found|source|according to|reference') { $detectedFold = "Pop" }
    elseif ($r -match 'hello|hi|hey|yo|sup|howdy') { $detectedFold = "He" }
    $path = Join-Path $script:MicroDir "$id.json"
    $existed = Test-Path $path
    $confidence = if ($script:TopicConfidence.ContainsKey($id)) { $script:TopicConfidence[$id] } else { 0.50 }
    Set-Content $path (@{ id=$id; subject=$title; fold=$detectedFold; loadCount=0; related=@(); sources=@();
        response=$Response.Substring(0,[Math]::Min(800,$Response.Length));
        confidence=$confidence;
        created=(Get-Date -Format "yyyy-MM-dd HH:mm:ss");
        updated=(Get-Date -Format "yyyy-MM-dd HH:mm:ss");
        tags=@{ fold=$detectedFold; tone=(Get-Tone $Topic); source="chat" } } | ConvertTo-Json) -Encoding UTF8
    $action = if ($existed) { "updated" } else { "created" }
    if ($script:TurnMeta -and $script:TurnMeta.MicronautActions -is [System.Collections.IList]) {
        $script:TurnMeta.MicronautActions += "micronaut: $id $action"
    }
    $script:TopicConfidence[$id] = $confidence
    $script:TopicLastResponse[$id] = $Response
    $script:LastMicronautId = $id
    Write-Console "micronaut: $id [$detectedFold] conf=$confidence" "System"
}

function Find-RelevantMicronaut($query, $lookback = 6) {
    if (-not (Test-Path $script:MicroDir)) { return $null }
    # Build a context window from recent conversation turns (user + assistant)
    $recent = @()
    if ($script:Conversation.Count -gt 0) {
        $start = [Math]::Max(0, $script:Conversation.Count - $lookback)
        for ($i = $start; $i -lt $script:Conversation.Count; $i++) {
            $recent += $script:Conversation[$i].content
        }
    }
    $context = ($query + " " + ($recent -join " ")).ToLower()
    $contextWords = ($context -split '\s+' | Where-Object { $_.Length -gt 3 }) | Sort-Object -Unique
    $best = $null
    $bestScore = 0
    foreach ($f in Get-ChildItem $script:MicroDir -Filter "*.json") {
        try {
            $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
            $corpus = ($d.response + " " + $d.subject + " " + $d.id).ToLower()
            $score = 0
            foreach ($w in $contextWords) {
                if ($corpus -match "\b$w\b") { $score++ }
            }
            # Bonus for provisional confidence state and recent update
            if ($d.confidence) { $score += [Math]::Min(2, $d.confidence) }
            if ($d.updated -and ((Get-Date) - (Get-Date $d.updated)).TotalHours -lt 1) { $score += 1 }
            if ($score -gt $bestScore) {
                $bestScore = $score
                $best = @{ File = $f; Data = $d; Score = $score }
            }
        } catch { }
    }
    # Threshold: require at least 2 matching content words or an explicit id match
    if ($bestScore -ge 2) { return $best }
    return $null
}

function Test-CorrectionSignal($text) {
    $t = $text.ToLower()
    $contradictionSignals = @('actually', 'no,', 'wrong', 'incorrect', 'not true', 'that is not', 'correction', 'fix', 'you said', 'Saturn is', 'mostly', 'primarily')
    $corroborationSignals = @('yes', 'that is right', 'correct', 'good answer', 'exactly', 'thank you')
    foreach ($s in $contradictionSignals) { if ($t -match [regex]::Escape($s)) { return 'contradiction' } }
    foreach ($s in $corroborationSignals) { if ($t -match [regex]::Escape($s)) { return 'corroboration' } }
    return $null
}

function Refine-Micronaut($MicronautFile, $newResponse, $signal = 'correction') {
    try {
        $d = Get-Content $MicronautFile.FullName -Raw | ConvertFrom-JsonSafe
        $id = $d.id
        $oldConfidence = if ($d.confidence) { $d.confidence } else { 0.50 }
        $confidence = $oldConfidence
        $evidence = if ($d.evidence) { $d.evidence } else { @() }
        $contradictions = if ($d.contradictions) { $d.contradictions } else { @() }

        if ($signal -eq 'contradiction') {
            $confidence = [Math]::Max(0.10, $confidence - 0.05)
            $contradictions += @{
                timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
                prior_response = $d.response
                correction = $newResponse
                note = "User-supplied correction"
            }
        } elseif ($signal -eq 'corroboration') {
            $confidence = [Math]::Min(0.85, $confidence + 0.05)
            $evidence += @{
                timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
                note = "User corroboration"
            }
        }

        # Ensure properties exist before setting (legacy files may lack them)
        $d | Add-Member -NotePropertyName 'response' -NotePropertyValue '' -Force -ErrorAction SilentlyContinue
        $d | Add-Member -NotePropertyName 'confidence' -NotePropertyValue 0.50 -Force -ErrorAction SilentlyContinue
        $d | Add-Member -NotePropertyName 'updated' -NotePropertyValue '' -Force -ErrorAction SilentlyContinue
        $d | Add-Member -NotePropertyName 'evidence' -NotePropertyValue @() -Force -ErrorAction SilentlyContinue
        $d | Add-Member -NotePropertyName 'contradictions' -NotePropertyValue @() -Force -ErrorAction SilentlyContinue
        $d | Add-Member -NotePropertyName 'provenance' -NotePropertyValue @() -Force -ErrorAction SilentlyContinue

        $d.response = $newResponse.Substring(0, [Math]::Min(800, $newResponse.Length))
        $d.confidence = $confidence
        $d.updated = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        $d.evidence = $evidence
        $d.contradictions = $contradictions
        $d.provenance += @{
            action = "refine"
            signal = $signal
            confidence_delta = $confidence - $oldConfidence
            timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        }

        Set-Content $MicronautFile.FullName ($d | ConvertTo-Json) -Encoding UTF8
        $script:TopicConfidence[$id] = $confidence
        $script:TopicLastResponse[$id] = $newResponse
        $script:LastMicronautId = $id
        if ($script:TurnMeta -and $script:TurnMeta.MicronautActions -is [System.Collections.IList]) {
            $script:TurnMeta.MicronautActions += "refine: $id $signal conf=$([Math]::Round($confidence,2))"
        }
        Write-Console "refine: $id $signal conf=$([Math]::Round($confidence,2))" "Phase"
        return $true
    } catch {
        Write-Console "refine failed: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Derive-Domain($query) {
    # Derive a snake_case domain name suitable for micronaut_factory.exe create.
    # Strips stop words, keeps the top 2-3 meaningful tokens, joins with underscores.
    $stop = @('a','an','the','is','are','was','were','be','been','being','have','has','had','do','does','did','will','would','could','should','may','might','must','shall','can','need','dare','ought','used','to','of','in','on','at','by','for','with','about','against','between','into','through','during','before','after','above','below','from','up','down','out','off','over','under','again','further','then','once','here','there','when','where','why','how','all','each','few','more','most','other','some','such','no','nor','not','only','own','same','so','than','too','very','just','and','but','if','or','because','as','until','while','what','which','who','whom','this','that','these','those','am','it','its','my','our','their','your','his','her')
    $tokens = ($query.ToLower() -split '[\s\?\.\,\!\:\;\(\)\[\]\{\}]+' | Where-Object { $_.Length -gt 2 -and $stop -notcontains $_ } | Select-Object -First 3)
    if (-not $tokens) { return $null }
    $domain = ($tokens -join '_') -replace '[^a-z0-9_]', '' -replace '_+', '_'
    if ($domain.Length -gt 40) { $domain = $domain.Substring(0,40) }
    return $domain
}

function Invoke-QuantumTrinityResearch($query) {
    # Try the Python bot first (richer multi-source research).
    $pyBot = Join-Path $script:Root 'scripts\research_bot.py'
    if (Test-Path $pyBot) {
        try {
            $tmpOut = Join-Path $env:TEMP ("qtbot_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
            $tmpErr = Join-Path $env:TEMP ("qtbot_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
            $proc = Start-Process -FilePath 'py.exe' `
                -ArgumentList "`"$pyBot`" `"$query`" --json" `
                -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
            $exited = $proc.WaitForExit(90000)
            if (-not $exited) {
                try { $proc.Kill() } catch { }
                Write-Console "research_bot.py: timed out after 90s" "System"
            } elseif ($proc.ExitCode -eq 0) {
                $out = Get-Content $tmpOut -Raw -Encoding UTF8
                $resp = $out | ConvertFrom-JsonSafe
                if ($resp.status -eq 'success' -and $resp.sources) {
                    $parts = @()
                    if ($resp.sources.wikipedia) { $parts += "WIKIPEDIA: " + $resp.sources.wikipedia.summary }
                    if ($resp.sources.arxiv) {
                        foreach ($paper in $resp.sources.arxiv | Select-Object -First 2) {
                            $parts += "ARXIV: " + $paper.title + " (" + $paper.url + "): " + $paper.abstract
                        }
                    }
                    if ($resp.sources.semantic_scholar) {
                        foreach ($paper in $resp.sources.semantic_scholar | Select-Object -First 2) {
                            $parts += "SCHOLAR: " + $paper.title + " (" + $paper.url + "): " + $paper.abstract
                        }
                    }
                    if ($resp.sources.google_news) {
                        foreach ($article in $resp.sources.google_news | Select-Object -First 2) {
                            $parts += "NEWS: " + $article.title + " [" + $article.source + "] " + $article.link
                        }
                    }
                    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
                    $candidate = ($parts -join "`n`n").Trim()
                    if ($candidate.Length -ge 40) {
                        return $candidate
                    }
                }
            } else {
                $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
                Write-Console "research_bot.py: exit $($proc.ExitCode) $err" "System"
            }
            Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        } catch {
            Write-Console "research_bot.py: failed ($($_.Exception.Message))" "System"
        }
    }

    # Fallback to the C++ Quantum Trinity worker.
    $exe = Join-Path $script:Root 'bin\Quantum\quantum_trinity.exe'
    if (-not (Test-Path $exe)) { return $null }
    try {
        $tmpOut = Join-Path $env:TEMP ("qt_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
        $tmpErr = Join-Path $env:TEMP ("qt_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $proc = Start-Process -FilePath $exe `
            -ArgumentList "--quiet `"$query`"" `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(60000)
        if (-not $exited) {
            try { $proc.Kill() } catch { }
            Write-Console "quantum_trinity: timed out after 60s" "System"
            Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
            return $null
        }
        if ($proc.ExitCode -ne 0) {
            $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
            Write-Console "quantum_trinity: exit $($proc.ExitCode) $err" "System"
            Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
            return $null
        }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        $resp = $out | ConvertFrom-JsonSafe
        if ($resp.status -eq 'success' -and $resp.results) {
            $webParts = $resp.results | Where-Object { $_ -like 'WEB:*' }
            if ($webParts) {
                return ($webParts -join "`n").Substring(4).Trim()
            }
            return ($resp.results -join "`n").Trim()
        }
    } catch {
        Write-Console "quantum_trinity: research failed ($($_.Exception.Message))" "System"
    }
    return $null
}

function Invoke-QuantumPersonality($Mode, $Argument, $UserId) {
    $exe = Join-Path $script:Root 'bin\Quantum\quantum_personality.exe'
    if (-not (Test-Path $exe)) {
        Add-Sys "quantum_personality.exe not found"
        return
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("qp_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
        $tmpErr = Join-Path $env:TEMP ("qp_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $json = @{ operation = 'interact'; user_id = $UserId; input = $Argument } | ConvertTo-Json -Compress
        $proc = Start-Process -FilePath $exe `
            -ArgumentList "--quiet", $json `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(30000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'quantum_personality timed out'; return }
        if ($proc.ExitCode -ne 0) { Add-Sys "quantum_personality exit $($proc.ExitCode)"; return }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        $resp = $out | ConvertFrom-JsonSafe
        Add-AI $resp.response "QuantumPersonality"
        $top = $resp.activated_personas | Select-Object -First 1
        if ($top) {
            Add-Sys "persona: $($top.id) activation $([math]::Round($top.activation_level, 2))"
        }
    } catch {
        Add-Sys "quantum_personality failed: $($_.Exception.Message)"
    }
}

function Invoke-QuantumHybrid($Operation, $Argument) {
    $exe = Join-Path $script:Root 'bin\Quantum\quantum_hybrid.exe'
    if (-not (Test-Path $exe)) {
        Add-Sys "quantum_hybrid.exe not found"
        return
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("qh_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
        $tmpErr = Join-Path $env:TEMP ("qh_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $json = if ($Operation -eq 'analyze_code') {
            @{ operation = $Operation; code = $Argument } | ConvertTo-Json -Compress
        } elseif ($Operation -eq 'extract_patterns') {
            @{ operation = $Operation; text = $Argument } | ConvertTo-Json -Compress
        } else {
            @{ operation = 'process'; input = $Argument; mode = 'hybrid' } | ConvertTo-Json -Compress
        }
        $proc = Start-Process -FilePath $exe `
            -ArgumentList "--quiet", $json `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(30000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'quantum_hybrid timed out'; return }
        if ($proc.ExitCode -ne 0) { Add-Sys "quantum_hybrid exit $($proc.ExitCode)"; return }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        $resp = $out | ConvertFrom-JsonSafe
        Add-AI $resp.output "QuantumHybrid"
        if ($resp.analysis) {
            $keys = $resp.analysis | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
            Add-Sys "hybrid engines: $($keys -join ', ')"
        }
    } catch {
        Add-Sys "quantum_hybrid failed: $($_.Exception.Message)"
    }
}

function Invoke-GptOssActivationFromKuhul($modelDir = 'E:\models\GPT-OSS', $outDir = 'E:\models\GPT-OSS\activations', $layer = 0, $seqLen = 64, $scaleEmbed = 0.0001) {
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    $python = if ($py) { 'py.exe' } else { 'python.exe' }
    $args = @('scripts\gptoss_layer_forward.py', "`"$modelDir`"", '--out-dir', "`"$outDir`"", '--layer', $layer, '--seq-len', $seqLen, '--scale-embed', $scaleEmbed)
    $tmpOut = Join-Path $env:TEMP ('gptoss_fwd_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    $tmpErr = Join-Path $env:TEMP ('gptoss_fwd_err_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    try {
        $proc = Start-Process -FilePath $python -ArgumentList $args -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(120000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'gptoss_layer_forward timed out'; return $null }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        if ($proc.ExitCode -ne 0) { Add-Sys "gptoss_layer_forward exit $($proc.ExitCode)"; return $null }
        Add-Sys "GPT-OSS layer $layer activation shards ready"
        return Invoke-AsxRamFromKuhul -QShard (Join-Path $outDir 'layer_00_q.xshard') -KShard (Join-Path $outDir 'layer_00_k.xshard') -VShard (Join-Path $outDir 'layer_00_v.xshard') -Config (Join-Path $outDir 'model_config.json') -passes 1 -Prefetch
    } catch {
        Add-Sys "gptoss_layer_forward failed: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-AsxRamFromKuhul($QShard, $KShard, $VShard, $Config = 'model_config.json', $passes = 1, [switch]$Prefetch) {
    $exe = Join-Path $script:Root 'bin\asx_ram_v2.exe'
    if (-not (Test-Path $exe)) { $exe = Join-Path $script:Root 'bin\asx_ram.exe' }
    if (-not (Test-Path $exe)) {
        Add-Sys "asx_ram executable not found"
        return $null
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("asxram_out_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $tmpErr = Join-Path $env:TEMP ("asxram_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $args = @($QShard, $KShard, $VShard)
        if ($exe -match 'asx_ram_v2') { $args += $Config }
        $args += [string]$passes
        if ($Prefetch) { $args += '--prefetch' }
        $proc = Start-Process -FilePath $exe -ArgumentList $args `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(120000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'asx_ram timed out'; return $null }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        if ($proc.ExitCode -ne 0) {
            Add-Sys "asx_ram exit $($proc.ExitCode): $err"
            return $null
        }
        Add-Sys "asx_ram: compute complete ($passes pass(es), prefetch=$Prefetch)"
        return $out
    } catch {
        Add-Sys "asx_ram failed: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-AsxGemmFromKuhul($Shard, $Experts = '0', $passes = 1) {
    $exe = Join-Path $script:Root 'bin\asx_gemm.exe'
    if (-not (Test-Path $exe)) { $exe = Join-Path $script:Root 'bin\Quantum\asx_gemm.exe' }
    if (-not (Test-Path $exe)) {
        Add-Sys "asx_gemm executable not found"
        return $null
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("asxgemm_out_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $tmpErr = Join-Path $env:TEMP ("asxgemm_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $args = @($Shard, $Experts, [string]$passes)
        $proc = Start-Process -FilePath $exe -ArgumentList $args `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(300000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'asx_gemm timed out'; return $null }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        if ($proc.ExitCode -ne 0) {
            Add-Sys "asx_gemm exit $($proc.ExitCode): $err"
            return $null
        }
        Add-Sys "asx_gemm: compute complete (experts=$Experts, passes=$passes)"
        return $out
    } catch {
        Add-Sys "asx_gemm failed: $($_.Exception.Message)"
        return $null
    }
}

$script:NncKServerProcess = $null

function Start-NncKServer($ModelDir = 'E:\models\GPT-OSS', $Port = 1235) {
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    $python = if ($py) { 'py.exe' } else { 'python.exe' }
    $scriptPath = Join-Path $script:Root 'scripts\nnc_k_server.py'
    if (-not (Test-Path $scriptPath)) { Add-Sys "nnc_k_server.py not found"; return $null }
    if ($script:NncKServerProcess -and -not $script:NncKServerProcess.HasExited) {
        Add-Sys "nnc-k server already running (PID $($script:NncKServerProcess.Id))"
        return $script:NncKServerProcess
    }
    $tmpOut = Join-Path $env:TEMP ('nnc_k_server_out_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    $tmpErr = Join-Path $env:TEMP ('nnc_k_server_err_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    try {
        $proc = Start-Process -FilePath $python -ArgumentList @("`"$scriptPath`"", '--model-dir', "`"$ModelDir`"", '--port', $Port) `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory $script:Root
        $script:NncKServerProcess = $proc
        Add-Sys "nnc-k server starting on port $Port (PID $($proc.Id))"
        return $proc
    } catch {
        Add-Sys "nnc-k server start failed: $($_.Exception.Message)"
        return $null
    }
}

function Stop-NncKServer {
    if (-not $script:NncKServerProcess) { Add-Sys "nnc-k server not running"; return }
    try {
        if (-not $script:NncKServerProcess.HasExited) {
            $script:NncKServerProcess.Kill()
            $script:NncKServerProcess.WaitForExit(5000)
        }
        Add-Sys "nnc-k server stopped"
    } catch {
        Add-Sys "nnc-k server stop failed: $($_.Exception.Message)"
    }
    $script:NncKServerProcess = $null
}

function Invoke-NncKChat($Prompt, $Port = 1235, $MaxTokens = 32) {
    try {
        $body = @{ model = 'gpt-oss-20b'; messages = @(@{ role = 'user'; content = $Prompt }); max_tokens = $MaxTokens; temperature = 0.7 } | ConvertTo-Json -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$Port/v1/chat/completions")
        $req.Method = 'POST'; $req.ContentType = 'application/json'; $req.Timeout = 300000
        $req.ContentLength = $bytes.Length
        $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
        $rs = $req.GetResponse().GetResponseStream()
        $rd = New-Object System.IO.StreamReader($rs)
        $json = $rd.ReadToEnd(); $rd.Close()
        $resp = $json | ConvertFrom-JsonSafe
        $text = $resp.choices[0].message.content
        $timings = $resp.timings
        Add-AI $text
        Add-Sys "nnc-k: tokens=$($timings.tokens_generated) tps=$($timings.tps) first-token=$($timings.first_token_ms)ms"
        return $resp
    } catch {
        Add-Sys "nnc-k chat failed: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-Benchmark($Gguf, $ModelDir = 'E:\models\GPT-OSS', $MaxTokens = 32, $Prompts = 3) {
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    $python = if ($py) { 'py.exe' } else { 'python.exe' }
    $scriptPath = Join-Path $script:Root 'scripts\bench_llama_vs_xshard.py'
    if (-not (Test-Path $scriptPath)) { Add-Sys "bench_llama_vs_xshard.py not found"; return $null }
    $tmpOut = Join-Path $env:TEMP ('bench_out_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    $tmpErr = Join-Path $env:TEMP ('bench_err_' + [System.Guid]::NewGuid().ToString('N') + '.txt')
    try {
        $proc = Start-Process -FilePath $python -ArgumentList @("`"$scriptPath`"", '--gguf', "`"$Gguf`"", '--model-dir', "`"$ModelDir`"", '--max-tokens', $MaxTokens, '--n-prompts', $Prompts) `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory $script:Root
        $exited = $proc.WaitForExit(600000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'benchmark timed out'; return $null }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        if ($proc.ExitCode -ne 0) { Add-Sys "benchmark exit $($proc.ExitCode): $err"; return $null }
        Add-AI $out
        return $out
    } catch {
        Add-Sys "benchmark failed: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-FileDropFromKuhul($paths) {
    if (-not $paths) { return $null }
    try {
        $results = Invoke-FileDropIngest -Paths $paths -WorkerRoot $script:Root
        foreach ($r in $results) {
            Add-Sys "file-drop: $($r.Path) → lane:$($r.Lane) size:$($r.Size)"
        }
        $binaryCount = ($results | Where-Object { $_.Lane -eq 'binary_analysis' }).Count
        $codeCount = ($results | Where-Object { $_.Lane -eq 'code_analysis' }).Count
        $semanticCount = ($results | Where-Object { $_.Lane -eq 'semantic_ingest' }).Count
        $computeCount = ($results | Where-Object { $_.Lane -eq 'compute_attention' }).Count
        $summary = "file-drop summary: binary=$binaryCount code=$codeCount semantic=$semanticCount compute=$computeCount"
        Add-Sys $summary
        return $results
    } catch {
        Add-Sys "file-drop failed: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-QuantumGrammar($Operation, $Argument) {
    $exe = Join-Path $script:Root 'bin\Quantum\quantum_grammar.exe'
    if (-not (Test-Path $exe)) {
        Add-Sys "quantum_grammar.exe not found"
        return
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("qg_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
        $tmpErr = Join-Path $env:TEMP ("qg_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $json = if ($Operation -eq 'generate') {
            @{ operation = $Operation; grammar_type = if ($Argument) { $Argument } else { 'all' } } | ConvertTo-Json -Compress
        } else {
            @{ operation = 'parse'; grammar_type = 'all'; input = $Argument } | ConvertTo-Json -Compress
        }
        $proc = Start-Process -FilePath $exe `
            -ArgumentList "--quiet", $json `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(30000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'quantum_grammar timed out'; return }
        if ($proc.ExitCode -ne 0) { Add-Sys "quantum_grammar exit $($proc.ExitCode)"; return }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        $resp = $out | ConvertFrom-JsonSafe
        if ($Operation -eq 'generate') {
            $lines = $resp.results | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name | ForEach-Object { "$_`: $($resp.results.$_)" }
            Add-AI ($lines -join "`n") "QuantumGrammar"
        } else {
            $lines = $resp.results | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -in @('gbnf','ebnf','peg') } | ForEach-Object { "$($_.Name)`: $($resp.results.($_.Name))" }
            Add-AI ($lines -join "`n") "QuantumGrammar"
        }
    } catch {
        Add-Sys "quantum_grammar failed: $($_.Exception.Message)"
    }
}

function Invoke-QuantumMicroAgents($Mode, $Argument, $UserId) {
    $exe = Join-Path $script:Root 'bin\Quantum\quantum_microagents.exe'
    if (-not (Test-Path $exe)) {
        Add-Sys "quantum_microagents.exe not found"
        return
    }
    try {
        $tmpOut = Join-Path $env:TEMP ("qma_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
        $tmpErr = Join-Path $env:TEMP ("qma_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
        $json = @{ operation = 'process'; input = $Argument; session_id = $UserId; mode = if ($Mode) { $Mode } else { 'orchestrated' } } | ConvertTo-Json -Compress
        $proc = Start-Process -FilePath $exe `
            -ArgumentList "--quiet", $json `
            -NoNewWindow -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
        $exited = $proc.WaitForExit(30000)
        if (-not $exited) { try { $proc.Kill() } catch { }; Add-Sys 'quantum_microagents timed out'; return }
        if ($proc.ExitCode -ne 0) { Add-Sys "quantum_microagents exit $($proc.ExitCode)"; return }
        $out = Get-Content $tmpOut -Raw -Encoding UTF8
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        $resp = $out | ConvertFrom-JsonSafe
        if ($resp.combined_candidate) {
            Add-AI $resp.combined_candidate "QuantumMicroAgents"
        }
        if ($resp.candidates) {
            $names = $resp.candidates | ForEach-Object { $_.template } | Select-Object -Unique
            Add-Sys "microagents activated: $($names -join ', ')"
        }
        if ($resp.authority_boundary) {
            Add-Sys "authority: $($resp.authority_boundary)"
        }
    } catch {
        Add-Sys "quantum_microagents failed: $($_.Exception.Message)"
    }
}

function Research-And-MintMicronaut($query, $route, $turnTick) {
    # Semantic page fault: no micronaut matched.
    #
    # Authority boundary (frozen):
    #   - LFM / GPT / model node may ONLY propose candidate text.
    #   - The runtime (PowerShell + C#) owns Save-Micronaut persistence helpers,
    #     but contract-level micronaut creation authority belongs to:
    #       • BOSS           — authors / verifies / promotes contracts
    #       • MicronautManager  — runtime curator that commits normalized contracts
    #   - CHEESE judges collapsed edges; it does not create micronauts.
    #   - The model node NEVER writes, updates, merges, or promotes micronauts.
    #
    # This function acquires candidate knowledge from web/model-node sources,
    # then asks the runtime to mint a provisional micronaut. The returned text
    # is supplied back to K'UHUL/XCFE so the original query can be re-routed.
    $candidate = $null

    # 1. Try the Quantum Trinity C++ web-research worker first.
    try {
        $candidate = Invoke-QuantumTrinityResearch $query
        if ($candidate -and $candidate.Length -ge 40) {
            $script:TurnMeta.Sources.Web = $true
            $script:TurnMeta.Sources.Local = $false
            Write-Console "research: quantum_trinity web results retrieved" "System"
        }
    } catch {
        $script:TurnMeta.Sources.Web = $false
        $script:TurnMeta.Sources.Local = $true
        Write-Console "research: quantum_trinity unavailable ($($_.Exception.GetType().Name))" "System"
    }

    # 2. Legacy fallback: direct DuckDuckGo instant answer API (no extra dependency).
    if (-not $candidate -or $candidate.Length -lt 40) {
        try {
            $sw = New-Object System.Net.WebClient
            $sw.Encoding = [System.Text.Encoding]::UTF8
            $sr = $sw.DownloadString("https://api.duckduckgo.com/?q=$([System.Uri]::EscapeDataString($query))&format=json&no_html=1")
            $so = $sr | ConvertFrom-JsonSafe
            if ($so.AbstractText) {
                $script:TurnMeta.Sources.Web = $true
                $script:TurnMeta.Sources.Local = $false
                $candidate = $so.AbstractText
                Write-Console "research: web abstract retrieved" "System"
            }
        } catch {
            $script:TurnMeta.Sources.Web = $false
            $script:TurnMeta.Sources.Local = $true
            Write-Console "research: web search unavailable, falling back to model node ($($_.Exception.GetType().Name))" "System"
        }
    }

    # 3. If web gave nothing, ask the model node for a focused research summary
    if (-not $candidate -or $candidate.Length -lt 40) {
        try {
            $researchPrompt = @"
You are a research assistant populating a semantic knowledge base. Answer the following question concisely and factually. Do NOT prepend metadata like Intent/Brain/Confidence. Just give the answer.

Question: $query
"@
            $body = @{ messages = @(
                @{ role = "system"; content = "Provide a concise factual answer suitable for a knowledge base." }
                @{ role = "user"; content = $researchPrompt }
            ); model = $script:ActiveModel; max_tokens = $script:MaxTk; temperature = 0.3; stream = $false } | ConvertTo-Json -Depth 4 -Compress
            $req = [System.Net.HttpWebRequest]::Create("$($script:Endpoint)/v1/chat/completions")
            $req.Method = "POST"; $req.ContentType = "application/json"; $req.Timeout = 120000
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $req.ContentLength = $bytes.Length
            $s = $req.GetRequestStream()
            $s.Write($bytes, 0, $bytes.Length)
            $s.Close()
            $rs = $req.GetResponse().GetResponseStream()
            $rd = New-Object System.IO.StreamReader($rs)
            $json = $rd.ReadToEnd()
            $rd.Close()
            $resp = $json | ConvertFrom-JsonSafe
            $candidate = $resp.choices[0].message.content
            Write-Console "research: model node generated candidate" "System"
        } catch { Write-Console "research: model node failed ($($_.Exception.Message))" "Error" }
    }

    if (-not $candidate -or $candidate.Length -lt 20) {
        return $null
    }

    # 3. Scaffold a formal .micronaut package via the factory (runtime authority)
    $domain = Derive-Domain $query
    if ($domain) {
        try {
            $factory = Join-Path $script:Root "bin\micronaut_factory.exe"
            if (Test-Path $factory) {
                $proc = Start-Process -FilePath $factory -ArgumentList "create", $domain -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $env:TEMP "factory_out.txt") -RedirectStandardError (Join-Path $env:TEMP "factory_err.txt")
                if ($proc.ExitCode -eq 0) {
                    $script:TurnMeta.MicronautActions += "factory: $domain scaffolded"
                    Write-Console "factory: $domain scaffolded" "System"
                } else {
                    Write-Console "factory: create $domain returned exit code $($proc.ExitCode)" "System"
                }
            }
        } catch { Write-Console "factory: invocation failed ($($_.Exception.Message))" "System" }
    }

    # 4. Mint JSON micronaut from candidate knowledge for the NNC-K runtime.
    #    If a relevant provisional micronaut already exists for this conversation
    #    topic, refine it instead of spawning another unrelated one.
    $existing = Find-RelevantMicronaut $query 8
    if ($existing) {
        $refined = Refine-Micronaut $existing.File $candidate 'corroboration'
        if ($refined) {
            $script:TurnMeta.Sources.Web = $true
            $script:TurnMeta.Sources.Local = $false
            Write-Console "research: refined existing $($existing.Data.id)" "Phase"
            return $candidate
        }
    }
    Save-Micronaut $query $candidate "Sek"

    # 5. Reload manager/index so the new micronaut is visible
    if ($script:MicronautManager) {
        try { $null = $script:MicronautManager.LoadToRegister() } catch { }
    }

    # 5. Re-register with MicronautRegister directly
    $fs = Get-ChildItem $script:MicroDir -Filter "*.json" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($fs -and $script:MicronautRegister) {
        try {
            $json = Get-Content $fs.FullName -Raw | ConvertFrom-JsonSafe
            $term = ($candidate -split '\s+') | Where-Object { $_.Length -gt 4 } | Select-Object -First 2
            $subj = if ($term) { ($term -join '-').ToLower() } else { "research-response" }
            $mn = [NeuralGrammar.Core.MicronautNode]::new()
            $mn.GetType().GetProperty("Id").SetValue($mn, "auto_" + [System.Guid]::NewGuid().ToString("N").Substring(0,12))
            $mn.GetType().GetProperty("Subject").SetValue($mn, $subj)
            $mn.GetType().GetProperty("Capability").SetValue($mn, "chat")
            $mn.GetType().GetProperty("Phase").SetValue($mn, [NeuralGrammar.Core.FoldPhase]::Sek)
            $mn.GetType().GetProperty("Source").SetValue($mn, "research")
            $script:MicronautRegister.Register($mn)
            Write-Console "register: research micronaut registered" "Phase"
        } catch { Write-Console "register: research micronaut failed ($($_.Exception.Message))" "Error" }
    }

    return $candidate
}



function Invoke-ElizaCapability($query) {
    $qlower = $query.ToLower()
    $clusters = @(
        @{ fold='FAMILY'; kw=@('mother','father','mom','dad','parent','family'); resp=@('Tell me more about your family.','How does that make you feel about your parents?','When did you first notice this about your family?') },
        @{ fold='EMOTION'; kw=@('feel','feeling','felt'); resp=@('Why do you feel that way?','Have you felt this way before?','What does that feeling feel like?') },
        @{ fold='EMOTION'; kw=@('sad','unhappy','depressed','cry','lonely'); resp=@('I am sorry you feel sad. Can you tell me more?','What happened that made you sad?','Do you often feel this way?') },
        @{ fold='EMOTION'; kw=@('happy','glad','joy','good','better'); resp=@('What makes you feel happy?','Is there a reason you feel better?','I am glad to hear that. Can you elaborate?') },
        @{ fold='EMOTION'; kw=@('worry','worried','anxious','fear','afraid','scared'); resp=@('What worries you most?','Why does that worry you?','Have you discussed your worries with someone?') },
        @{ fold='THOUGHT'; kw=@('think','thought','believe','opinion','idea'); resp=@('Why do you think that?','What makes you believe that?','Have you always thought this way?') },
        @{ fold='MEMORY'; kw=@('remember','memory','recall','forgot','forget','dream','night'); resp=@('What do you remember about that?','Is there something else you remember?','What does that suggest to you?') },
        @{ fold='SOCIAL'; kw=@('friend','people','someone','everyone','nobody','relationship'); resp=@('Tell me about your relationships.','Who is important to you?','How do you feel about your friends?') },
        @{ fold='PROBLEM'; kw=@('problem','issue','trouble','difficult','hard','help','need','want','wish'); resp=@('What seems to be the problem?','What would help you?','How would you like things to change?') },
        @{ fold='OPEN'; kw=@('yes','no','maybe'); resp=@('Can you tell me more?','Why do you say that?','Please go on.') }
    )
    foreach ($c in $clusters) {
        foreach ($k in $c.kw) {
            if ($qlower -match "$k") {
                $idx = Get-Random -Maximum $c.resp.Count
                $captures = @($k)
                $m = [regex]::Match($qlower, "\S*\s*$k\s*\S*")
                if ($m.Success) { $captures = @($m.Value.Trim()) }
                return [PSCustomObject]@{
                    Match = $k
                    Fold = $c.fold
                    Captures = $captures
                    Intent = 'reflective_question'
                    Text = $c.resp[$idx]
                    Confidence = 0.75
                    Evidence = @()
                    Relations = @()
                }
            }
        }
    }
    # Fallback
    $f = @('Can you tell me more about that?','How does that make you feel?','What do you mean by that?','That is interesting. Can you elaborate?','Why do you say that?','I see. Please continue.')
    return [PSCustomObject]@{
        Match = 'open'
        Fold = 'OPEN'
        Captures = @($query.Split(' ')[0..([Math]::Min(2, ($query.Split(' ').Count - 1)))] -join ' ')
        Intent = 'general_prompt'
        Text = $f[(Get-Random -Maximum $f.Count)]
        Confidence = 0.50
        Evidence = @()
        Relations = @()
    }
}


# ============================================================================
# Micronaut Reasoning Kernel -- RECOGNIZE â†’ RELATE â†’ REMEMBER â†’ ARTICULATE
# ============================================================================


# ============================================================================
# Micronaut Affective Kernel -- computational emotion as control variables
# ============================================================================
function New-AffectiveState {
    return [PSCustomObject]@{ valence=0.0; arousal=0.5; curiosity=0.6; confidence=0.5; concern=0.3; frustration=0.0; skepticism=0.4; attachment=0.0; history=@() }
}


# ============================================================================
# HEliza — He semantic/context engine + ELIZA deterministic pattern accelerator
# ============================================================================
$script:HeContext = @{ entities = @(); topics = @(); lastIntent = ''; turnCount = 0; associations = @{} }

function Invoke-HElizaNode($query) {
    $script:HeContext.turnCount++
    $ctx = $script:HeContext
    $qlower = $query.ToLower()
    
    # Step 1: ELIZA pattern recognition (match → capture → transform)
    $elizaResult = Invoke-ElizaCapability $query
    $match = if ($elizaResult.Match) { $elizaResult.Match } else { 'open' }
    $fold = if ($elizaResult.Fold) { $elizaResult.Fold } else { 'OPEN' }
    $elizaText = if ($elizaResult.Text) { $elizaResult.Text } else { '' }
    
    # Step 2: He-style entity extraction from query
    $words = $qlower -split '\s+' | Where-Object { $_.Length -gt 3 }
    $newEntities = @($words | Where-Object { $_ -notin $ctx.entities -and $_ -notmatch '^(what|when|where|why|how|the|that|this|with|from|about|your|there|their)$' })
    if ($newEntities.Count -gt 0) {
        $ctx.entities += $newEntities
        if ($ctx.entities.Count -gt 50) { $ctx.entities = $ctx.entities[-30..-1] }
    }
    
    # Step 3: Context association — find related past entities
    $relatedEntities = @()
    foreach ($e in $ctx.entities) {
        if ($qlower -match [regex]::Escape($e) -and $e -ne $match) {
            $relatedEntities += $e
        }
    }
    if ($ctx.associations.ContainsKey($match)) {
        $relatedEntities += $ctx.associations[$match]
    }
    
    # Step 4: Build context-aware contribution
    $contextualText = $elizaText
    
    # If we've seen a topic before, reference continuity
    $topics = $ctx.topics | Where-Object { $qlower -match [regex]::Escape($_) }
    if ($topics -and $topics.Count -gt 0) {
        $topTopic = $topics[0]
        if (-not $ctx.associations.ContainsKey($match)) {
            $ctx.associations[$match] = @()
        }
        if ($topTopic -notin $ctx.associations[$match]) {
            $ctx.associations[$match] += $topTopic
        }
    }
    
    # Track conversation topic
    $intentEntities = @($elizaResult.Captures | Where-Object { $_ -and $_ -ne 'open' })
    if ($intentEntities.Count -gt 0) {
        $ctx.topics += $intentEntities[0]
        if ($ctx.topics.Count -gt 20) { $ctx.topics = $ctx.topics[-10..-1] }
    }
    $ctx.lastIntent = $elizaResult.Intent
    
    # Step 5: Articulate with entity substitution
    if ($relatedEntities.Count -gt 0 -and $ctx.turnCount -gt 1) {
        $randEntity = $relatedEntities[(Get-Random -Maximum $relatedEntities.Count)]
        if ($contextualText -match 'that\b' -and $randEntity) {
            $contextualText = $contextualText -replace '\bthat\b', $randEntity
        }
    }
    
    # Confidence: pattern match + context = higher confidence
    $baseConfidence = if ($match -ne 'open') { 0.70 } else { 0.45 }
    $contextBoot = if ($relatedEntities.Count -gt 0) { 0.10 } else { 0 }
    $confidence = [Math]::Min(0.85, $baseConfidence + $contextBoot)
    
    Write-Console "HEliza: $match [$fold] -> $contextualText (conf=$([Math]::Round($confidence,2)))" "Tick"
    
    return [PSCustomObject]@{
        Subject = 'heliza'
        Capability = 'heliza'
        Fold = $fold
        Match = $match
        Captures = $elizaResult.Captures
        Intent = $elizaResult.Intent
        Text = $contextualText
        Confidence = $confidence
        Evidence = @($relatedEntities)
        Relations = @(@{ Entity=$match; Associated=$relatedEntities; Turn=$ctx.turnCount })
        ContextState = @{ entities = $ctx.entities.Count; topics = $ctx.topics.Count; associations = $ctx.associations.Count }
    }
}

function Step-Affect($micronaut, $recognition, $existingState) {
    # Load or initialize affective state
    $state = if ($existingState) { $existingState } else { New-AffectiveState }
    $d = $micronaut.d
    # Pattern-driven state transitions
    if ($recognition -and $recognition.Patterns) {
        foreach ($patt in $recognition.Patterns) {
            $name = $patt.Name
            # Analyze pattern type and adjust affect
            if ($name -match 'contradiction|error|inconsistent') {
                $state.frustration = [Math]::Min(1.0, $state.frustration + 0.08)
                $state.curiosity   = [Math]::Min(1.0, $state.curiosity + 0.12)
            }
            if ($name -match 'new|novel|discover') {
                $state.curiosity   = [Math]::Min(1.0, $state.curiosity + 0.20)
                $state.arousal     = [Math]::Min(1.0, $state.arousal + 0.10)
            }
            if ($name -match 'confirm|verify|match') {
                $state.confidence  = [Math]::Min(1.0, $state.confidence + 0.10)
                $state.skepticism  = [Math]::Max(0.0, $state.skepticism - 0.05)
            }
            if ($name -match 'uncertain|unknown|ambiguous') {
                $state.confidence  = [Math]::Max(0.0, $state.confidence - 0.15)
                $state.curiosity   = [Math]::Min(1.0, $state.curiosity + 0.15)
            }
            if ($name -match 'emotion:sad|emotion:worry|emotion:fear') {
                $state.concern     = [Math]::Min(1.0, $state.concern + 0.12)
                $state.valence     = [Math]::Max(-1.0, $state.valence - 0.15)
                $state.arousal     = [Math]::Min(1.0, $state.arousal + 0.18)
            }
            if ($name -match 'emotion:happy|emotion:joy') {
                $state.valence     = [Math]::Min(1.0, $state.valence + 0.12)
                $state.arousal     = [Math]::Max(0.0, $state.arousal - 0.05)
                $state.confidence  = [Math]::Min(1.0, $state.confidence + 0.05)
            }
            if ($name -match 'problem|need|help') {
                $state.concern     = [Math]::Min(1.0, $state.concern + 0.10)
                $state.frustration = [Math]::Min(1.0, $state.frustration + 0.05)
            }
            if ($name -match 'social|friend|relationship') {
                $state.attachment  = [Math]::Min(1.0, $state.attachment + 0.08)
                $state.valence     = [Math]::Min(1.0, $state.valence + 0.05)
            }
        }
    }
    # Decay toward baseline (slow normalization)
    $state.arousal     = $state.arousal * 0.95 + 0.5 * 0.05
    $state.frustration = $state.frustration * 0.90
    $state.skepticism  = $state.skepticism * 0.97 + 0.4 * 0.03
    # Clamp
    foreach ($prop in @('valence','arousal','curiosity','confidence','concern','frustration','skepticism','attachment')) {
        $val = $state.$prop
        $state.$prop = [Math]::Max(-1.0, [Math]::Min(1.0, $val))
    }
    # Record history
    $state.history += [PSCustomObject]@{ Tick = $script:TickCounter; Valence = $state.valence; Arousal = $state.arousal; Curiosity = $state.curiosity; Confidence = $state.confidence }
    if ($state.history.Count -gt 20) { $state.history = $state.history | Select-Object -Last 15 }
    Write-Console "    AFFECT: v=$([Math]::Round($state.valence,2)) c=$([Math]::Round($state.confidence,2)) q=$([Math]::Round($state.curiosity,2))" "Tick"
    return $state
}

function Apply-AffectiveModulation($state, $reasoning) {
    # Modify reasoning behavior based on affective state
    $mod = [PSCustomObject]@{
        extraRelationTraversal = ($state.curiosity -gt 0.75)
        hedgeResponse = ($state.confidence -lt 0.35)
        seekVerification = ($state.skepticism -gt 0.60 -or $state.concern -gt 0.55)
        abandonPath = ($state.frustration -gt 0.70)
        elaborateMore = ($state.curiosity -gt 0.80 -and $state.arousal -gt 0.60)
        collaborative = ($state.attachment -gt 0.30)
    }
    $modData = @()
    if ($mod.extraRelationTraversal) { $modData += "traverse_extra_relations" }
    if ($mod.hedgeResponse) { $modData += "hedge_response" }
    if ($mod.seekVerification) { $modData += "seek_verification" }
    if ($mod.elaborateMore) { $modData += "elaborate" }
    if ($mod.collaborative) { $modData += "collaborative" }
    if ($modData.Count -gt 0) {
        Write-Console "    MODULATION: $($modData -join ', ')" "Tick"
    }
    return $mod
}
function Step-Recognize($micronaut, $query) {
    $d = $micronaut.d; $qlower = $query.ToLower()
    $patterns = $d.patterns
    if (-not $patterns) {
        # Fallback: match against recognize keywords
        $keywords = $d.recognize.keywords
        if (-not $keywords) { return $null }
        $matched = @($keywords | Where-Object { $qlower -match "\b$_\b" })
        if ($matched.Count -eq 0) { return $null }
        return [PSCustomObject]@{ Matched = $matched; Patterns = @(); Entities = @(); Confidence = [Math]::Min(0.85, 0.40 + $matched.Count * 0.15) }
    }
    $matchedPatterns = @()
    $foundEntities = @()
    foreach ($pname in $patterns.PSObject.Properties.Name) {
        $p = $patterns.$pname
        $syns = $p.synonyms
        foreach ($s in $syns) {
            if ($qlower -match "\b$s\b") {
                $matchedPatterns += [PSCustomObject]@{
                    Name = $pname; Match = $s; Neighborhood = $p.neighborhood
                    Intent = $p.intent; Responses = $p.responses
                    Properties = $p.psobject.Properties | Where-Object { $_.Name -notin @('synonyms','neighborhood','intent','responses') } | ForEach-Object { "$($_.Name)=$($_.Value)" }
                }
                $foundEntities += "$pname"
                break
            }
        }
    }
    if ($matchedPatterns.Count -eq 0) { return $null }
    return [PSCustomObject]@{
        Matched = $foundEntities
        Patterns = $matchedPatterns
        Entities = $foundEntities
        Confidence = [Math]::Min(0.90, 0.50 + $matchedPatterns.Count * 0.10)
    }
}

function Step-Relate($recognition, $micronaut) {
    $d = $micronaut.d; $relations = $d.relations
    if (-not $relations -or -not $recognition.Entities) { return [PSCustomObject]@{ Related = @(); Confidence = 0 } }
    $related = @()
    foreach ($r in $relations) {
        $rStr = $r -join ' '
        foreach ($e in $recognition.Entities) {
            $eName = ($e -split ':')[1]
            if ($rStr.ToLower() -match $eName) {
                $related += [PSCustomObject]@{ Entity = $eName; Relation = $r; Direction = if ($r[0] -eq $eName) { 'subject' } else { 'object' } }
            }
        }
    }
    return [PSCustomObject]@{
        Related = $related
        Confidence = if ($related.Count -gt 0) { [Math]::Min(0.85, 0.30 + $related.Count * 0.15) } else { 0 }
    }
}

function Step-Remember($recognition, $micronaut) {
    $d = $micronaut.d; $memories = $d.memories
    if (-not $memories -or $memories.Count -eq 0) { return [PSCustomObject]@{ Recalled = @(); Confidence = 0 } }
    $recalled = @()
    foreach ($m in $memories) {
        $score = 0
        foreach ($e in $recognition.Entities) {
            $eName = ($e -split ':')[1].Replace('_', ' ')
            if ($m.ToLower() -match $eName) { $score++ }
        }
        if ($score -gt 0) { $recalled += [PSCustomObject]@{ Memory = $m; Score = $score } }
    }
    return [PSCustomObject]@{
        Recalled = $recalled | Sort-Object Score -Descending
        Confidence = if ($recalled.Count -gt 0) { [Math]::Min(0.80, 0.30 + $recalled.Count * 0.20) } else { 0 }
    }
}

function Step-Articulate($recognition, $relation, $memory, $micronaut) {
    $d = $micronaut.d; $subj = $d.subject; $cap = $d.capability
    if ($cap -eq "heliza") {
        return Invoke-HElizaNode $query
    }
    if ($cap -eq "eliza") {
        $p = $recognition.Patterns[0]
        if ($p.Responses) {
            $idx = Get-Random -Maximum $p.Responses.Count
            $text = $p.Responses[$idx]
            # Modulate ELIZA articulation by affect if available
            if ($micronaut.d.affectiveState) {
                $a = $micronaut.d.affectiveState
                if ($a.concern -gt 0.60) { $text = "I sense this concerns you. " + $text }
                if ($a.valence -lt -0.20) { $text += " This seems difficult for you." }
                if ($a.curiosity -gt 0.80) { $text += " What else comes to mind?" }
            }
            return [PSCustomObject]@{
                Intent = $p.Intent; Neighborhood = $p.Neighborhood
                Text = $text; Match = $p.Match
                Evidence = @(); Confidence = 0.75
            }
        }
    }
    # Knowledge carrier: compose from memories + relations
    $facts = @()
    if ($memory.Recalled) { $facts += @($memory.Recalled | Select-Object -First 2 | ForEach-Object { $_.Memory }) }
    if ($relation.Related) { $facts += @($relation.Related | Select-Object -First 2 | ForEach-Object { ($_.Relation -join ' ') }) }
    $best = if ($facts.Count -gt 0) { $facts[0] } else { ($d.response -split '[\.!?]')[0].Trim() }
    return [PSCustomObject]@{
        Intent = 'knowledge_lookup'
        Text = $best; Match = $recognition.Entities[0]
        Evidence = $facts; Confidence = [Math]::Max($relation.Confidence, $memory.Confidence)
    }
}

function Invoke-ReasoningKernel($micronaut, $query, $fold) {
    Write-Console "  REASONING: $($micronaut.d.subject) [$fold]" "Tick"
    # Load affective state from previous cycle (survived Xul collapse)
    $d = $micronaut.d
    $affect = if ($d.affectiveState) {
        [PSCustomObject]$d.affectiveState
    } else {
        New-AffectiveState
    }
    # Apply temperament defaults if not yet initialized
    if ($d.temperament -and (-not $affect._initialized)) {
        foreach ($k in $d.temperament.PSObject.Properties.Name) {
            if ($affect.PSObject.Properties[$k]) { $affect.$k = $d.temperament.$k }
        }
        $affect | Add-Member -NotePropertyName "_initialized" -NotePropertyValue $true -Force
    }

    # RECOGNIZE -- affect broadens detection threshold
    Write-Console "    STATE: v=$([Math]::Round($affect.valence,2)) c=$([Math]::Round($affect.confidence,2)) q=$([Math]::Round($affect.curiosity,2)) s=$([Math]::Round($affect.skepticism,2))" "Tick"
    $recog = Step-Recognize $micronaut $query
    if (-not $recog) { return $null }
    # Arousal broadens recognition: detect more patterns
    if ($affect.arousal -gt 0.55 -and $recog.Patterns.Count -gt 0) {
        Write-Console "    arousal=$([Math]::Round($affect.arousal,2)) broadened pattern detection" "Tick"
    }
    Write-Console "    RECOGNIZE: $($recog.Entities -join ', ') (conf=$([Math]::Round($recog.Confidence,2)))" "Tick"

    # RELATE -- curiosity decides traversal depth
    $rel = Step-Relate $recog $micronaut
    if ($affect.curiosity -gt 0.60 -and $rel.Related.Count -gt 0) {
        Write-Console "    RELATE: $($rel.Related.Count) relation(s) [curiosity=$([Math]::Round($affect.curiosity,2))]" "Tick"
    } elseif ($rel.Related.Count -gt 0) {
        Write-Console "    RELATE: $($rel.Related.Count) relation(s)" "Tick"
    }

    # REMEMBER -- concern and skepticism filter memories
    $mem = Step-Remember $recog $micronaut
    if ($affect.concern -gt 0.50) {
        # Boost risk/conflict memory priority
        $filteredMemories = @($mem.Recalled | Where-Object { $_.Memory -match 'collapse|danger|critical|error|contradiction|problem' })
        if ($filteredMemories.Count -gt 0) {
            Write-Console "    REMEMBER: concern=$([Math]::Round($affect.concern,2)) boosted $($filteredMemories.Count) risk memories" "Tick"
        }
    }
    if ($mem.Recalled.Count -gt 0) {
        Write-Console "    REMEMBER: $($mem.Recalled.Count) memories recalled" "Tick"
    }

    # UPDATE AFFECT -- stimulus updates state
    $affect = Step-Affect $micronaut $recog $affect

    # MODULATE -- state modifies articulation
    $modulation = Apply-AffectiveModulation $affect $rel
    if ($modulation.seekVerification -and $mem.Recalled.Count -lt 2) {
        $modulation.qualifyResponse = $true
    }

    # ARTICULATE
    $art = Step-Articulate $recog $rel $mem $micronaut
    if ($modulation.qualifyResponse -or $modulation.seekVerification) {
        $art.Text = "[verifying] " + $art.Text
    }
    Write-Console "    ARTICULATE: $($art.Intent) -> $($art.Text.Substring(0,[Math]::Min(60,$art.Text.Length)))..." "Tick"

    # Store updated affect back on micronaut for Xul survival
    $d | Add-Member -NotePropertyName 'affectiveState' -Value $affect -Force

    return [PSCustomObject]@{
        Subject = $micronaut.d.subject; Capability = $micronaut.d.capability
        Fold = $fold; Match = $art.Match; Intent = $art.Intent
        Text = $art.Text; Confidence = $art.Confidence
        Evidence = $art.Evidence; Captures = @($recog.Entities)
        Relations = $rel.Related; Neighborhood = $art.Neighborhood
    }
}
function Invoke-Micronaut($micronaut, $query, $fold) {
    $d = $micronaut.d
    $resp = $d.response
    $subj = $d.subject
    $cap = $d.capability
    if (-not $resp -and $cap -ne "eliza") { return $null }
    # Use reasoning kernel when structured patterns exist
    if ($d.patterns -and $d.patterns.PSObject.Properties.Count -gt 0) {
        return Invoke-ReasoningKernel $micronaut $query $fold
    }
    # Dispatch to capability-specific handler
    if ($cap -eq "heliza") {
        return Invoke-HElizaNode $query
    }
    if ($cap -eq "eliza") {
        $result = Invoke-ElizaCapability $query
        return [PSCustomObject]@{
            Subject=$subj; Capability=$cap; Fold=$result.Fold; Match=$result.Match
            Captures=$result.Captures; Intent=$result.Intent; Text=$result.Text
            Confidence=$result.Confidence; Evidence=$result.Evidence; Relations=$result.Relations
        }
    }
    # Default: keyword-match against stored response text
    if ($resp.Length -lt 20) { return $null }
    $qWords = ($query -split '\s+' | Where-Object { $_.Length -gt 3 }) -join '|'
    $matched = [regex]::Matches($resp, $qWords, 'IgnoreCase')
    $score = $matched.Count
    if ($score -eq 0) { return $null }
    $sentences = $resp -split '(?<=[\.!?])\s+'
    $best = $sentences | Where-Object { $_ -match $qWords } | Select-Object -First 1
    $confidence = [Math]::Min(0.95, [Math]::Max(0.30, $score / ([Math]::Max(1, ($resp -split '\s+').Count) * 0.15)))
    $facts = @($matched | ForEach-Object { $_.Value } | Select-Object -Unique -First 3)
    return [PSCustomObject]@{
        Subject = $subj
        Capability = $cap
        Fold = $fold
        Match = $facts[0]
        Captures = $facts
        Intent = 'knowledge_lookup'
        Text = if ($best) { $best } else { ($resp -split '[\.!?]')[0].Trim() }
        Confidence = [Math]::Round($confidence, 3)
        Evidence = $facts
        Relations = @()
    }
}

function Dispatch-MicronautWorker($micronaut, $query, $contribution) {
    if (-not $script:XCFERuntime -or -not $script:XCFERuntime.MicronautRuntime) { return $null }
    $runtime = $script:XCFERuntime.MicronautRuntime
    $avail = $runtime.GetAvailability()
    $d = $micronaut.d

    # Build a minimal job envelope.
    $job = @{
        verb = if ($contribution.Intent) { $contribution.Intent } else { "process" }
        query = $query
        subject = $d.subject
        capability = $d.capability
        fold = $contribution.Fold
        match = $contribution.Match
        confidence = $contribution.Confidence
        text = $contribution.Text
    } | ConvertTo-Json -Depth 3

    # Prefer HTTP worker when the micronaut advertises an endpoint.
    if ($d.workerUrl -and $avail.HttpWorker) {
        try {
            $result = $runtime.RunHttpWorker($d.workerUrl, $job)
            if ($result.Success) {
                $parsed = $result.Output | ConvertFrom-JsonSafe
                return [PSCustomObject]@{
                    Text = $parsed.text ?? $parsed.output ?? $contribution.Text
                    Confidence = [double]($parsed.confidence ?? $contribution.Confidence)
                    Evidence = @($parsed.evidence ?? @())
                    Transport = "http"
                }
            }
        } catch { Write-Console "http worker dispatch failed: $($_.Exception.Message)" "Error" }
    }

    # Fall back to stdio worker if available.
    if ($avail.StdioWorker) {
        try {
            $manifest = $d | ConvertTo-Json -Depth 4
            $result = $runtime.RunWorker($job, $manifest)
            if ($result.Success) {
                $parsed = $result.Output | ConvertFrom-JsonSafe
                return [PSCustomObject]@{
                    Text = $parsed.text ?? $parsed.output ?? $contribution.Text
                    Confidence = [double]($parsed.confidence ?? $contribution.Confidence)
                    Evidence = @($parsed.evidence ?? @())
                    Transport = "stdio"
                }
            } else {
                Write-Console "stdio worker error: $($result.Error)" "Error"
            }
        } catch { Write-Console "stdio worker dispatch failed: $($_.Exception.Message)" "Error" }
    }

    return $null
}

function Submit-MicronautContribution($micronaut, $query, $contribution, $tick) {
    try {
        $fname = $micronaut.file
        $mfile = Join-Path $script:MicroDir $fname
        if (-not (Test-Path $mfile)) { return }
        $d = Get-Content $mfile -Raw | ConvertFrom-JsonSafe
        # Track separate lifecycle counts
        $execCount = if ($null -eq $d.executeCount) { 1 } else { [int]$d.executeCount + 1 }
        $contribCount = if ($null -eq $d.contributionCount) { 1 } else { [int]$d.contributionCount + 1 }
        $loadCount = if ($null -eq $d.loadCount) { 1 } else { [int]$d.loadCount + 1 }
        $d | Add-Member -NotePropertyName 'executeCount' -NotePropertyValue $execCount -Force
        $d | Add-Member -NotePropertyName 'contributionCount' -NotePropertyValue $contribCount -Force
        $d | Add-Member -NotePropertyName 'loadCount' -NotePropertyValue $loadCount -Force
        $d | Add-Member -NotePropertyName 'lastTick' -NotePropertyValue $tick -Force
        $d | Add-Member -NotePropertyName 'lastMatch' -NotePropertyValue $contribution.Match -Force
        $d | Add-Member -NotePropertyName 'lastIntent' -NotePropertyValue $contribution.Intent -Force
        $d | Add-Member -NotePropertyName 'lastConfidence' -NotePropertyValue $contribution.Confidence -Force
        $d | Add-Member -NotePropertyName 'lastFold' -NotePropertyValue $contribution.Fold -Force
        # Cap query snippet at 80 chars
        $d | Add-Member -MemberType NoteProperty -Name "lastQuery" -Value ($query.Substring(0, [Math]::Min(80, $query.Length))) -Force
        $d | ConvertTo-Json| Set-Content $mfile -Encoding UTF8
        # Update register quality
        if ($script:MicronautRegister) {
            try {
                $node = $script:MicronautRegister.Lookup($d.id)
                if ($node) { $node.GetType().GetProperty("Quality").SetValue($node, $contribCount) }
            } catch {}
        }
        Write-Console "micronaut: $($d.subject) contributed [exec=$execCount contrib=$contribCount]" "Tick"
    } catch { Write-Console "submit: $($_.Exception.Message)" "Error" }
}


# ============================================================================
# K'UHUL Program Executor -- loads .kuhul programs and executes fold structure
# ============================================================================
function Invoke-KuhulProgram($programPath, $input, $turnTick) {
    if (-not (Test-Path $programPath)) { return $null }
    $prog = Get-Content $programPath -Raw | ConvertFrom-JsonSafe
    if (-not $prog.folds) { return $null }
    $progName = if ($prog.meta -and $prog.meta.name) { $prog.meta.name } else { (Split-Path $programPath -Leaf) -replace '\.kuhul$','' }
    Write-Console "PROGRAM $progName `n" "Phase"
    # Runtime state dictionary
    $state = @{}
    $contributions = @()
    $programTick = $turnTick
    # Seed with input
    $state['input'] = @{ prompt = $input; query = $input }
    # Execute each fold in order
    foreach ($fold in $prog.folds) {
        $foldName = $fold.name
        Write-Console "  [$programTick] $foldName ENTER" "Phase"; $programTick++
        # Process fold nodes
        foreach ($node in $fold.nodes) {
            $result = Invoke-ProgramNode $node $state $input $foldName
            if ($result -and $result.Contributions) {
                $contributions += $result.Contributions
            }
        }
        # Xul writes collapse state
        if ($foldName -eq 'Xul') {
            Write-Console "  [$programTick] Xul collapse state written" "Phase"; $programTick++
            break
        }
    }
    # Return program result
    $emitData = if ($state.ContainsKey('result')) { $state['result'] } else { $null }
    return [PSCustomObject]@{
        Program = $progName; Contributions = $contributions
        State = $state; Result = $emitData; Tick = $programTick
    }
}

function Invoke-ProgramNode($node, $state, $input, $foldName) {
    $type = $node.type
    switch ($type) {
        'literal' {
            return $node.value
        }
        'assign' {
            $target = $node.target
            $value = Resolve-ProgramValue $node.value $state
            $state[$target] = $value
            return $null
        }
        'ref' {
            $name = $node.name
            if ($state.ContainsKey($name)) { return $state[$name] } else { return $null }
        }
        'call' {
            $callName = $node.name
            $args = @($node.args | ForEach-Object { Resolve-ProgramValue $_ $state })
            # Route calls to known handlers
            switch ($callName) {
                'invoke' {
                    return Invoke-ProgramMicronaut $args $input $foldName
                }
                'classify' {
                    $state['classification'] = @{ intent = if ($args.Count -gt 0) { $args[0] } else { "general" } }
                    return $null
                }
                'score' {
                    $state['scored'] = $args
                    return $null
                }
                'plan' {
                    $state['plan'] = $args
                    return $null
                }
                'implement' {
                    $state['code'] = "// Generated code placeholder"
                    return $null
                }
                'test' {
                    $state['tested'] = $true
                    return $null
                }
                'discover' {
                    $state['discovered'] = $args
                    return $null
                }
                'curate' {
                    $state['curated'] = $args
                    return $null
                }
                'verify' {
                    $state['verified'] = $args
                    return $null
                }
                'promote' {
                    $state['promoted'] = $args
                    return $null
                }
                'advise' {
                    return @{ suggestion = "refold" }
                }
                'reindex' {
                    $state['reindexed'] = $true
                    return $null
                }
                'fold' {
                    $state['next_fold'] = $args[0]
                    return $null
                }
                'collapse' {
                    $state['collapsed'] = $true
                    return $null
                }
                default {
                    Write-Console "  call: $callName (no handler)" "Tick"
                    return $null
                }
            }
        }
        'block' {
            $blockContribs = @()
            foreach ($n in $node.nodes) {
                $r = Invoke-ProgramNode $n $state $input $foldName
                if ($r -and $r.Contributions) { $blockContribs += $r.Contributions }
            }
            return @{ Contributions = $blockContribs }
        }
        'if' {
            $test = Evaluate-ProgramTest $node.test $state
            if ($test) {
                return Invoke-ProgramNode $node.then $state $input $foldName
            } elseif ($node.else) {
                return Invoke-ProgramNode $node.else $state $input $foldName
            }
            return $null
        }
        'emit' {
            $state['result'] = Resolve-ProgramValue $node.value $state
            return $null
        }
    }
    return $null
}

function Resolve-ProgramValue($value, $state) {
    if (-not $value) { return $null }
    if ($value -isnot [PSCustomObject] -and $value -isnot [hashtable]) { return $value }
    $t = ''; if ($value.type) { $t = $value.type } elseif ($value.PSObject.Properties['type'] -ne $null) { $t = $value.type }
    if ($t -eq 'literal') { return $value.value }
    if ($t -eq 'ref') {
        $n = $value.name
        $parts = $n.Split('.')
        $v = $state
        foreach ($p in $parts) {
            if (-not $v) { return $null }
            if ($v -is [PSCustomObject]) { $v = $v.$p }
            elseif ($v.ContainsKey($p)) { $v = $v[$p] }
            else { return $null }
        }
        return $v
    }
    return $value
}

function Evaluate-ProgramTest($test, $state) {
    if (-not $test) { return $true }
    $t = $test.type
    if ($t -eq 'ref') {
        $v = Resolve-ProgramValue $test $state
        if ($v -eq $true -or $v -eq 'true' -or $v -eq 1) { return $true }
        return $false
    }
    if ($t -eq 'op') {
        $left = Resolve-ProgramValue $test.args[0] $state
        $right = Resolve-ProgramValue $test.args[1] $state
        $op = $test.op
        if ($op -eq '==') { return $left -eq $right }
        if ($op -eq '<') { return $left -lt $right }
        if ($op -eq '>') { return $left -gt $right }
        if ($op -eq '>=') { return $left -ge $right }
        if ($op -eq '<=') { return $left -le $right }
    }
    return $false
}

function Invoke-ProgramMicronaut($args, $input, $foldName) {
    $micronautRef = if ($args.Count -gt 0) { $args[0] } else { "" }
    $query = if ($args.Count -gt 1) { $args[1] } else { $input }
    # Map program micronaut references to actual micronauts
    $nameMap = @{
        'MEM-u' = 'semantic-memory'
        'REASON-u' = 'black-hole'
        'VERIFY-u' = 'black-hole'
        'PLAN-u' = 'stellar-evolution-facts'
        'NET-u' = 'cosmic-distance-facts'
    }
    $actualName = if ($nameMap.ContainsKey($micronautRef)) { $nameMap[$micronautRef] } else { $micronautRef }
    Write-Console "    invoke $micronautRef -> $actualName" "Tick"
    # Find matching micronaut from Load-RelevantMicronauts
    $matches = Load-RelevantMicronauts $actualName
    if ($matches.Count -gt 0) {
        $m = $matches[0]
        $contrib = Invoke-Micronaut $m $query $foldName
        if ($contrib) {
            Submit-MicronautContribution $m $query $contrib $script:TickCounter
            return @{ Contributions = @($contrib) }
        }
    }
    return @{ Contributions = @() }
}
function Load-RelevantMicronauts($query) {
    # O(1) index lookup when available, filesystem fallback otherwise
    if ($script:MicronautManager -and $script:MicronautManager.Index) {
        $results = @()
        $qWords = ($query.ToLower() -split '\s+' | Where-Object { $_.Length -gt 3 }) | Sort-Object -Unique
        foreach ($desc in $script:MicronautManager.Index.All) {
            $score = 0
            $nameLower = $desc.Name.ToLower()
            foreach ($w in $qWords) {
                if ($nameLower -match "$w") { $score++ }
            }
            if ($score -gt 0) {
                $results += @{ Name = $desc.Name; Capability = $desc.Capability; Engine = $desc.Engine; Score = $score; Descriptor = $desc }
            }
        }
        return $results | Sort-Object Score -Descending | Select-Object -First 5
    }

    if (-not (Test-Path $script:MicroDir)) { return @() }
    $qWords = ($query.ToLower() -split '\s+' | Where-Object { $_.Length -gt 3 }) | Sort-Object -Unique
    $result = @()
    foreach ($f in Get-ChildItem $script:MicroDir -Filter "*.json") {
        try {
            $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
            $r = ($d.response + " " + $d.subject).ToLower()
            $score = 0
            foreach ($w in $qWords) { if ($r -match "\b$w\b") { $score++ } }
            if ($d.loadCount) { $score += [Math]::Min(3, $d.loadCount/2) }
            if ($score -ge 1) { $result += @{ file = $f.Name; score = $score; d = $d } }
        } catch { }
    }
    return $result | Sort-Object score -Descending | Select-Object -First 5
}
function Update-MicronautLinks {
    $micros = Get-ChildItem $script:MicroDir -Filter "*.json" | ForEach-Object {
        $d = $_; try { $j = Get-Content $d.FullName -Raw | ConvertFrom-JsonSafe; $_ | Add-Member -NotePropertyName LoadCount -NotePropertyValue ([int]($null -eq $j.loadCount ? 0 : $j.loadCount)) -Force -PassThru } catch { $_ | Add-Member -NotePropertyName LoadCount -NotePropertyValue 0 -Force -PassThru }
    } | Sort-Object LoadCount -Descending
    foreach ($f in $micros) {
        try {
            $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
            $words = ($d.response -split '\s+' | Where-Object { $_.Length -gt 4 } | Select-Object -Unique) -join ' '
            $related = @()
            foreach ($o in $micros | Where-Object { $_.Name -ne $f.Name }) {
                $od = Get-Content $o.FullName -Raw | ConvertFrom-JsonSafe; $score = 0
                $owords = $od.response -split '\s+'
                foreach ($w in $owords) { if ($w.Length -gt 4 -and $words -match "\b$w\b") { $score++ } }
                if ($score -ge 2) { $related += @{ id = $od.id; score = $score; subject = $od.subject } }
            }
            $d | Add-Member -MemberType NoteProperty -Name "related" -Value ($related | Sort-Object score -Descending | Select-Object -First 5) -Force
            $d.loadCount = [int]($null -eq $d.loadCount ? 0 : $d.loadCount)
            $d | Add-Member -MemberType NoteProperty -Name "sources" -Value @() -Force
            $d | ConvertTo-Json | Set-Content $f.FullName -Encoding UTF8
        } catch { }
    }
    # Emit semantic.link events
    $semanticLinks = @()
    foreach ($f in $micros) { try {
        $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
        foreach ($o in $micros | Where-Object { $_.Name -ne $f.Name }) { try {
            $od = Get-Content $o.FullName -Raw | ConvertFrom-JsonSafe
            if ($od.subject -and $d.subject -and $d.id -and $od.id) {
                $fTerms = $d.subject -split '\s+' | Where-Object { $_.Length -gt 2 }
                $oTerms = $od.subject -split '\s+' | Where-Object { $_.Length -gt 2 }
                $overlap = ($fTerms | Where-Object { $oTerms -contains $_ }).Count
                if ($overlap -gt 0) {
                    $score = $overlap / [Math]::Max($fTerms.Count, $oTerms.Count)
                    if ($score -ge 0.25) {
                        $semanticLinks += @{ type="semantic.link"; source=$d.id; target=$od.id; score=$score }
                    }
                }
            }
        } catch { } }
    } catch { } }
    if ($semanticLinks.Count -gt 0) {
        Add-Sys "semantic.link: $($semanticLinks.Count) links (avg score=$([Math]::Round(($semanticLinks | ForEach-Object { $_.score } | Measure-Object -Average).Average, 3)))"
    }

    # Emit semantic.cluster event
    $clusterCounts = @{}
    foreach ($f in $micros) { try {
        $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
        if ($d.subject) { $first = ($d.subject -split '\s+')[0]; if ($clusterCounts.ContainsKey($first)) { $clusterCounts[$first]++ } else { $clusterCounts[$first] = 1 } }
    } catch { } }
    if ($clusterCounts.Count -gt 0) {
        $topCluster = ($clusterCounts.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1)
        Add-Sys "semantic.cluster: $($topCluster.Key) ($($topCluster.Value)/$($micros.Count))"
    }

    Add-Sys "cross-linked: $($micros.Count)"
}
function Export-Micronauts($fmt = "md") {
    $dir = Join-Path $PSScriptRoot ".learning\exports"
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $micros = @(Get-ChildItem $script:MicroDir -Filter "*.json")
    if ($micros.Count -eq 0) { Add-Sys "export: none"; return }
    if ($fmt -eq "md") {
        $output = "# Micronauts`n_$(Get-Date -Format 'yyyy-MM-dd HH:mm')_`n`n"
        foreach ($f in $micros | Sort-Object LastWriteTime) {
            $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
            $output += "## $($d.subject)`n**$($d.fold)  loads=$($d.loadCount)**`n`n$($d.response)`n`n---`n`n"
        }
        $path = Join-Path $dir "micronauts-$(Get-Date -Format 'yyyyMMdd').md"
        $output | Set-Content $path -Encoding UTF8; Add-Sys "export: $path ($($micros.Count))"
    }
}

# Sidebar
function Refresh-ChatList {
    $chatListPanel.Children.Clear(); $script:ChatStore = @()
    $dir = Join-Path $PSScriptRoot ".learning\chats"
    if (Test-Path $dir) { foreach ($f in Get-ChildItem $dir -Filter "*.json" | Sort-Object LastWriteTime -Descending) { try { $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe; $script:ChatStore += @{ id = $d.id; title = $d.title } } catch { } } }
    foreach ($chat in $script:ChatStore) {
        $row = [System.Windows.Controls.Border]::new(); $row.Margin = '0,1,0,0'; $row.Padding = '6,4'; $row.Cursor = "Hand"
        $row.Background = '#0d1117'; $row.BorderBrush = '#30363d'; $row.BorderThickness = '1'; $row.CornerRadius = '4'
        $sp = [System.Windows.Controls.StackPanel]::new()
        $t = [System.Windows.Controls.TextBlock]::new(); $t.Text = $chat.title; $t.FontSize = 11; $t.Foreground = '#e2e8f0'; $t.TextTrimming = "CharacterEllipsis"
        $sp.Children.Add($t); $row.Child = $sp; $cid = $chat.id
        $row.Add_MouseDown({ Load-ChatById $cid; $sidebarOpen = $false; $sidebarPanel.Visibility = "Collapsed" }.GetNewClosure())
        $chatListPanel.Children.Add($row)
    }
}
function Load-ChatById($id) {
    $path = Join-Path $PSScriptRoot ".learning\chats\$id.json"
    if (Test-Path $path) { try { $d = Get-Content $path -Raw | ConvertFrom-JsonSafe; $script:Conversation = @(); $feed.Children.Clear(); $script:ExecutionTraces = @{}
        $script:CurrentChatId = $d.id; $script:FluxSessionId = $id
        if ($script:FluxStore) { $script:FluxStore.SessionId = $id }
        if ($script:FluxStore) {
            $fluxLoaded = $script:FluxStore.LoadSessionRawJson()
            foreach ($kv in $fluxLoaded.GetEnumerator()) { $script:ExecutionTraces[$kv.Key] = $kv.Value | ConvertFrom-JsonSafe }
        }
        foreach ($m in $d.messages) { $script:Conversation += @{ role = $m.role; content = $m.content; tick = $m.tick; meta = $m.meta }; if ($m.role -eq "user") { Add-User $m.content } elseif ($m.role -eq "assistant") { Add-AI $m.content $m.meta } }
        $chatTitleBar.Text = "  $($d.title)" } catch { } }
}
function Save-Chat {
    if ($script:Conversation.Count -eq 0) { return }
    if (-not $script:CurrentChatId) { $script:CurrentChatId = [System.Guid]::NewGuid().ToString("N").Substring(0,12) }
    $script:FluxSessionId = $script:CurrentChatId
    if ($script:FluxStore) { $script:FluxStore.SessionId = $script:FluxSessionId }
    $title = $script:Conversation | Where-Object { $_.role -eq "user" } | Select-Object -First 1 -ExpandProperty content
    if (-not $title) { $title = "Chat $(Get-Date -Format 'MM/dd HH:mm')" }
    if ($title.Length -gt 50) { $title = $title.Substring(0,47) + "..." }
    $dir = Join-Path $PSScriptRoot ".learning\chats"; if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $entry = @{ id = $script:CurrentChatId; title = $title; messages = $script:Conversation }
    $entry | ConvertTo-Json | Set-Content (Join-Path $dir "$($script:CurrentChatId).json") -Encoding UTF8
    if ($script:FluxStore) {
        foreach ($tick in $script:ExecutionTraces.Keys) {
            try {
                $json = $script:ExecutionTraces[$tick] | ConvertTo-Json -Depth 6
                $script:FluxStore.Save($tick, $json)
            } catch { }
        }
    }
    if ($script:Conversation.Count -ge 2) {
        $lt = $script:Conversation[-2..-1]; $um = $lt | Where-Object { $_.role -eq "user" } | Select-Object -First 1 -ExpandProperty content
        $am = $lt | Where-Object { $_.role -eq "assistant" } | Select-Object -First 1 -ExpandProperty content
        if ($um -and $am -and $am.Length -gt 20) { Save-Micronaut -Topic $um -Response $am -Fold (Get-Fold $um) }
    }
}
function New-Chat { if ($script:Conversation.Count -gt 0) { Save-Chat }; $script:Conversation = @(); $script:CurrentChatId = $null; $script:FluxSessionId = "default"; if ($script:FluxStore) { $script:FluxStore.SessionId = $script:FluxSessionId }; $script:ExecutionTraces = @{}; $script:TopicConfidence = @{}; $script:TopicLastResponse = @{}; $script:LastMicronautId = $null; $feed.Children.Clear(); $chatTitleBar.Text = "  New chat" }

function Save-FluxSession {
    if (-not $script:FluxStore) { Add-Sys "@flux store not initialized"; return }
    foreach ($tick in $script:ExecutionTraces.Keys) {
        try {
            $json = $script:ExecutionTraces[$tick] | ConvertTo-Json -Depth 6
            $script:FluxStore.Save($tick, $json)
        } catch { }
    }
    Add-Sys "@flux session '$($script:FluxStore.SessionId)' saved ($($script:ExecutionTraces.Count) traces)"
}

function Load-FluxSession($id) {
    if (-not $script:FluxStore) { Add-Sys "@flux store not initialized"; return }
    if ($id) { $script:FluxSessionId = $id; $script:FluxStore.SessionId = $id } else { $id = $script:FluxStore.SessionId }
    $script:ExecutionTraces = @{}
    $loaded = $script:FluxStore.LoadSessionRawJson()
    foreach ($kv in $loaded.GetEnumerator()) { $script:ExecutionTraces[$kv.Key] = $kv.Value | ConvertFrom-JsonSafe }
    Add-Sys "@flux session '$id' loaded ($($loaded.Count) traces)"
}

function Test-NodeContribution($json) {
    try {
        $validator = [NeuralGrammar.Core.Validation.NodeContributionValidator]::new((Join-Path $PSScriptRoot "schemas\node-contribution-v1.json"))
        $report = $validator.ValidateJson($json)
        if ($report.IsValid) { Add-Sys "NodeContribution: VALID" }
        else { Add-Sys ("NodeContribution: INVALID — " + ($report.Errors -join "; ")) }
        return $report
    } catch {
        Add-Sys "Validator error: $($_.Exception.Message)"
        return $null
    }
}

function Test-WorkerDispatch($query) {
    if (-not $script:XCFERuntime -or -not $script:XCFERuntime.MicronautRuntime) { Add-Sys "@runtime not initialized"; return }
    $runtime = $script:XCFERuntime.MicronautRuntime
    $job = @{ verb = "echo"; query = $query; payload = "manual-test" } | ConvertTo-Json -Depth 3
    $result = $runtime.DispatchJob($job)
    if ($result.Success) {
        Add-Sys ("Worker dispatch OK [" + $result.Transport + "]: " + $result.Output)
    } else {
        Add-Sys ("Worker dispatch FAILED [" + $result.Transport + "]: " + $result.Error)
    }
    return $result
}

function Invoke-FluxCommand($msg) {
    if ($msg -match '^/flux\s+save') { Save-FluxSession; return $true }
    if ($msg -match '^/flux\s+load\s+(\S+)') { Load-FluxSession $matches[1]; return $true }
    if ($msg -match '^/flux\s+sessions') {
        $sessions = $script:FluxStore.ListSessions()
        Add-Sys ("@flux sessions: " + ($sessions -join ", "))
        return $true
    }
    if ($msg -match '^/flux\s+export') {
        $json = $script:FluxStore.ExportSession()
        $path = Join-Path $script:DataDir "flux-$($script:FluxStore.SessionId)-$(Get-Date -Format 'yyyyMMddHHmmss').json"
        Set-Content $path $json -Encoding UTF8
        Add-Sys "@flux exported to $path"
        return $true
    }
    if ($msg -match '^/validate\s+(.+)') {
        Test-NodeContribution $matches[1]
        return $true
    }
    if ($msg -match '^/worker\s+test\s+(.+)') {
        Test-WorkerDispatch $matches[1]
        return $true
    }
    if ($msg -match '^/research\s+(.+)') {
        $q = $matches[1]
        Add-Sys "@node researching: $q"
        $candidate = Invoke-QuantumTrinityResearch $q
        if ($candidate) {
            Add-AI ($candidate.Substring(0, [Math]::Min(1200, $candidate.Length))) "QuantumTrinity"
            Add-Sys "research complete: $($candidate.Length) chars"
        } else {
            Add-Sys "research returned no results"
        }
        return $true
    }
    if ($msg -match '^/persona\s+(get|interact)\s+(.+)') {
        $mode = $matches[1]
        $rest = $matches[2]
        $userId = $script:CurrentUser?.Username ?? 'anonymous'
        Invoke-QuantumPersonality -Mode $mode -Argument $rest -UserId $userId
        return $true
    }
    if ($msg -match '^/hybrid\s+(process|analyze_code|extract_patterns)\s*(.*)') {
        $op = $matches[1]
        $arg = $matches[2]
        Invoke-QuantumHybrid -Operation $op -Argument $arg
        return $true
    }
    if ($msg -match '^/grammar\s+(parse|generate)\s*(.*)') {
        $op = $matches[1]
        $arg = $matches[2]
        Invoke-QuantumGrammar -Operation $op -Argument $arg
        return $true
    }
    if ($msg -match '^/microagents\s*(orchestrated|swarm)?\s*(.*)') {
        $mode = $matches[1]
        $arg = $matches[2]
        if (-not $arg) { Add-Sys '/microagents <mode?> <text>'; return $true }
        $userId = $script:CurrentUser?.Username ?? 'anonymous'
        Invoke-QuantumMicroAgents -Mode $mode -Argument $arg -UserId $userId
        return $true
    }
    if ($msg -match '^/nncserver\s+start\s*(.*)') {
        $rest = $matches[1].Trim()
        $md = if ($rest) { $rest } else { 'E:\models\GPT-OSS' }
        Start-NncKServer -ModelDir $md
        return $true
    }
    if ($msg -match '^/nncserver\s+stop') {
        Stop-NncKServer
        return $true
    }
    if ($msg -match '^/nncserver\s+chat\s+(.*)') {
        $arg = $matches[1]
        if (-not $arg) { Add-Sys '/nncserver chat <prompt>'; return $true }
        Invoke-NncKChat -Prompt $arg
        return $true
    }
    if ($msg -match '^/bench\s+(\S+)\s+(\S+)?\s*(\d+)?\s*(\d+)?') {
        $gguf = $matches[1]
        $mdir = if ($matches[2]) { $matches[2] } else { 'E:\models\GPT-OSS' }
        $maxtok = if ($matches[3]) { [int]$matches[3] } else { 32 }
        $n = if ($matches[4]) { [int]$matches[4] } else { 3 }
        Invoke-Benchmark -Gguf $gguf -ModelDir $mdir -MaxTokens $maxtok -Prompts $n
        return $true
    }
    return $false
}

# Auth
function Initialize-SessionDb { if (-not $script:SessionDb) { try { $p = Join-Path $PSScriptRoot ".users\database.json"; $script:SessionDb = New-Object NeuralGrammar.Core.UserDatabase($p) } catch { } } }
function Show-LoginDialog {
    Initialize-SessionDb
    $dlg = New-Object Windows.Window; $dlg.Title = "Sign In"; $dlg.Width = 380; $dlg.Height = 280
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window
    $dlg.Background = '#0d1117'; $dlg.Foreground = '#e2e8f0'; $dlg.FontFamily = "Consolas"
    $dlg.ResizeMode = "NoResize"; $dlg.WindowStyle = "SingleBorderWindow"; $dlg.Topmost = $true
    $g = [Windows.Controls.Grid]::new(); $g.Margin = '14'
    for ($i=0;$i-lt6;$i++) { $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)); $g.RowDefinitions[$i].Height = [Windows.GridLength]::new(28) }
    $g.RowDefinitions[4].Height = [Windows.GridLength]::new(36)
    $l1 = New-Object Windows.Controls.Label; $l1.Content = "Username:"; $l1.Foreground = '#58a6ff'; $l1.FontSize = 12
    [Windows.Controls.Grid]::SetRow($l1,0); $g.Children.Add($l1)
    $t1 = New-Object Windows.Controls.TextBox; $t1.Background = '#000'; $t1.Foreground = '#fff'; $t1.BorderBrush = '#30363d'
    [Windows.Controls.Grid]::SetRow($t1,1); $g.Children.Add($t1)
    $l2 = New-Object Windows.Controls.Label; $l2.Content = "Password:"; $l2.Foreground = '#58a6ff'; $l2.FontSize = 12
    [Windows.Controls.Grid]::SetRow($l2,2); $g.Children.Add($l2)
    $t2 = New-Object Windows.Controls.PasswordBox; $t2.Background = '#000'; $t2.Foreground = '#fff'; $t2.BorderBrush = '#30363d'
    [Windows.Controls.Grid]::SetRow($t2,3); $g.Children.Add($t2)
    $bp = New-Object Windows.Controls.StackPanel; $bp.Orientation = "Horizontal"; $bp.Margin = '0,4,0,0'
    $b1 = New-Object Windows.Controls.Button; $b1.Content = "Login"; $b1.Background = '#3b82f6'; $b1.Foreground = 'White'; $b1.FontWeight = "Bold"; $b1.Width = 90; $b1.Height = 28; $b1.Margin = '0,0,8,0'
    $b2 = New-Object Windows.Controls.Button; $b2.Content = "Register"; $b2.Background = '#238636'; $b2.Foreground = 'White'; $b2.FontWeight = "Bold"; $b2.Width = 90; $b2.Height = 28
    $bp.Children.Add($b1); $bp.Children.Add($b2)
    [Windows.Controls.Grid]::SetRow($bp,4); $g.Children.Add($bp)
    $m = New-Object Windows.Controls.TextBlock; $m.Text = ""; $m.Foreground = '#86efac'; $m.FontSize = 11
    [Windows.Controls.Grid]::SetRow($m,5); $g.Children.Add($m)
    $b1.Add_Click({
        $user = $script:SessionDb.Authenticate($t1.Text, $t2.Password)
        if ($user) { $script:CurrentUser = $user; $script:CurrentSession = $script:SessionDb.CreateSession($user)
            $f = Join-Path $PSScriptRoot ".users\.last_session"; $null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($f))
            $script:CurrentSession.Token | Set-Content $f -NoNewline -ErrorAction SilentlyContinue; Update-ProfileUI; $dlg.Close()
        } else { $m.Text = "Invalid"; $m.Foreground = '#da3633' }
    })
    $b2.Add_Click({ $u = $script:SessionDb.CreateUser($t1.Text, $t2.Password); if ($u) { $m.Text = "Created $($u.Username)"; $m.Foreground = '#3fb950' } else { $m.Text = "Exists"; $m.Foreground = '#da3633' } })
    $dlg.Content = $g; Show-ThemedDialog $dlg
}

function Show-ProfileSettings {
    if (-not $script:CurrentUser) { Show-LoginDialog; return }
    $dlg = New-Object Windows.Window; $dlg.Title = "Profile"; $dlg.Width = 500; $dlg.Height = 400
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window
    $dlg.Background = '#0d1117'; $dlg.Foreground = '#e2e8f0'; $dlg.FontFamily = "Consolas"
    $g = [Windows.Controls.Grid]::new(); $g.Margin = '14'
    $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)); $g.RowDefinitions[0].Height = [Windows.GridLength]::new(30)
    $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)); $g.RowDefinitions[1].Height = [Windows.GridLength]::new(1, [Windows.GridUnitType]::Star)
    $title = New-Object Windows.Controls.TextBlock; $title.Text = "Profile | $($script:CurrentUser.Username) | $($script:CurrentUser.Role)"
    $title.FontSize = 14; $title.FontWeight = "Bold"; $title.Foreground = '#58a6ff'
    [Windows.Controls.Grid]::SetRow($title,0); $g.Children.Add($title)
    $tabs = New-Object Windows.Controls.TabControl; $tabs.Background = '#0d1117'; $tabs.Foreground = '#e2e8f0'; $tabs.BorderBrush = '#30363d'
    $ts = [Windows.Controls.StackPanel]::new(); $ts.Margin = '8'
    $ts.Children.Add((New-Object Windows.Controls.TextBlock | ForEach-Object { $_.Text = "Capabilities: $($script:CurrentUser.GrantedCapabilities -join ', ')"; $_.FontSize = 11; $_.Foreground = '#8b949e'; $_ }))
    $ts.Children.Add((New-Object Windows.Controls.TextBlock | ForEach-Object { $_.Text = "Saved chats: $($script:ChatStore.Count)"; $_.FontSize = 11; $_.Foreground = '#8b949e'; $_ }))
    $bk = New-Object Windows.Controls.Button; $bk.Content = "+ API Key"; $bk.Background = '#238636'; $bk.Foreground = 'White'; $bk.Height = 28; $bk.Margin = '0,8,0,0'
    $bk.Add_Click({ if ($script:SessionDb) { $k = $script:SessionDb.CreateApiKey($script:CurrentUser.Id, "UI key"); [Windows.Clipboard]::SetText($k.Key); [Windows.MessageBox]::Show("Key copied!", "API") | Out-Null } })
    $ts.Children.Add($bk)
    $t1 = New-Object Windows.Controls.TabItem; $t1.Header = "Account"; $t1.Background = "#161b22"; $t1.Foreground = "#e2e8f0"; $t1.Content = $ts; $tabs.Items.Add($t1)
    $ms = [Windows.Controls.StackPanel]::new(); $ms.Margin = '8'; $ms.Background = '#0d1117'
    $lm = @(); foreach ($d in @("$env:USERPROFILE\.lmstudio\models","$env:LOCALAPPDATA\lm-studio\models","models")) { if (Test-Path $d) { $lm += Get-ChildItem $d -Recurse -Filter "*.gguf" -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name } }
    if ($lm) { foreach ($m in $lm | Select-Object -First 5) { $ms.Children.Add((New-Object Windows.Controls.TextBlock | ForEach-Object { $_.Text = "  - $m"; $_.FontSize = 10; $_.Foreground = '#86efac'; $_ })) } }
    else { $ms.Children.Add((New-Object Windows.Controls.TextBlock | ForEach-Object { $_.Text = "No local models found"; $_.FontSize = 10; $_.Foreground = '#8b949e'; $_ })) }
    $t2 = New-Object Windows.Controls.TabItem; $t2.Header = "Models"; $t2.Background = "#161b22"; $t2.Foreground = "#e2e8f0"; $t2.Content = $ms; $tabs.Items.Add($t2)
    [Windows.Controls.Grid]::SetRow($tabs,1); $g.Children.Add($tabs)
    $closeBtn = New-Object Windows.Controls.Button; $closeBtn.Content = "Close"; $closeBtn.Background = '#3b82f6'; $closeBtn.Foreground = 'White'; $closeBtn.Height = 30; $closeBtn.Width = 100
    $closeBtn.Add_Click({ $dlg.Close() }); [Windows.Controls.Grid]::SetRow($closeBtn,2); $g.Children.Add($closeBtn)
    $dlg.Content = $g; Show-ThemedDialog $dlg
}
function Logout-User {
    if ($script:CurrentSession -and $script:SessionDb) { try { $script:SessionDb.DestroySession($script:CurrentSession.Token) } catch { } }
    $f = Join-Path $PSScriptRoot ".users\.last_session"; if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    $script:CurrentUser = $null; $script:CurrentSession = $null; Update-ProfileUI
}
function Update-ProfileUI {
    if ($script:CurrentUser) { $profileIcon.Text = $script:CurrentUser.Username.Substring(0,1).ToUpper(); $profileName.Text = $script:CurrentUser.Username; $profileStatus.Text = "OK"; $userNameText.Text = " $($script:CurrentUser.Username)"
    } else { $profileIcon.Text = "?"; $profileName.Text = "Not signed in"; $profileStatus.Text = ""; $userNameText.Text = "" }
}
function Restore-Session {
    $f = Join-Path $PSScriptRoot ".users\.last_session"
    if (Test-Path $f) { try { $token = Get-Content $f -Raw -ErrorAction SilentlyContinue
        if ($token) { Initialize-SessionDb; $user = $script:SessionDb.ValidateSession($token.Trim())
            if ($user) { $script:CurrentUser = $user; $script:CurrentSession = @{ Token = $token.Trim() }; return $true } } } catch { } }
    return $false
}

# Attachments
$attachBtn.Add_Click({
    $ofd = New-Object Microsoft.Win32.OpenFileDialog; $ofd.Filter = "All|*.*"; $ofd.Multiselect = $true
    if ($ofd.ShowDialog()) { foreach ($p in $ofd.FileNames) { if ($script:Attachments.Count -lt 5 -and $script:Attachments -notcontains $p) { $script:Attachments += $p } } }
})

# Send
$script:SplashCtx = Show-SplashScreen
Start-Sleep -Milliseconds 200
Update-Splash $script:SplashCtx "Initializing runtime..." "Loading modules"
$modelSelector.SelectedItem = $script:ActiveModel
$modelSelector.Add_SelectionChanged({
    $sel = $modelSelector.SelectedItem
    if ($sel -and $sel -ne $script:ActiveModel) {
        $script:ActiveModel = $sel
        if ($sel -eq "gpt2") {
            $modelSelector.BorderBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(248,81,73))
            $modelSelector.BorderThickness = [Windows.Thickness]::new(2)
        } else {
            $modelSelector.BorderBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(48,54,61))
            $modelSelector.BorderThickness = [Windows.Thickness]::new(1)
        }
        Write-Console "Model: $sel" "Model"
    }
})

# ===== MICRONAUT REGISTER INIT =====
try {
    $script:MicronautRegister = [NeuralGrammar.Core.MicronautRegister]::new()
    if ($script:MicronautRegister) { Add-Sys "Register: initialized" }
Update-Splash $script:SplashCtx "XCFE runtime connected" "Register initialized"
    $script:XCFERuntime = [NeuralGrammar.Core.XCFERuntime]::new()
    $script:XCFERuntime.MicronautRegister = $script:MicronautRegister
    Write-Console "Register: $($script:MicronautRegister.Count) base nodes" "Phase"
} catch { Add-Error "Register init: $($_.Exception.Message)" }

try {
    $mutation = [NeuralGrammar.Core.XCFEMutation]::new($script:DataDir)
    $seeded = $mutation.SeedDomainMicronauts()
    if ($seeded -gt 0) {
        Write-Console "Seeded: $seeded domain micronauts" "System"
Update-Splash $script:SplashCtx "Micronauts seeded" "Domain knowledge loaded"
    }
} catch { Write-Console "Seed: $($_.Exception.Message)" "Error" }

Write-Console "Register: $($script:MicronautRegister.Count) nodes active" "Phase"
$progCount = @(Get-ChildItem (Join-Path $PSScriptRoot "schemas\programs") -Filter "*.kuhul" -ErrorAction SilentlyContinue).Count
Write-Console "Programs: $progCount .kuhul files loaded" "System"
Update-Splash $script:SplashCtx "K'UHUL programs loaded" "Schema runtime ready"
Write-Console "Search: hybrid index ready" "System"
; $btnUser = $window.FindName('BtnUser')
$userNameText = $window.FindName('UserNameText')
$sidebarPanel = $window.FindName('SidebarPanel'); $chatListPanel = $window.FindName('ChatListPanel')
$btnShowSidebar = $window.FindName('BtnShowSidebar'); $btnToggleSidebar = $window.FindName('BtnToggleSidebar')
$btnNewChat = $window.FindName('BtnNewChat')
$profileName = $window.FindName('ProfileName'); $profileStatus = $window.FindName('ProfileStatus')
$profileIcon = $window.FindName('ProfileIcon'); $profileBox = $window.FindName('ProfileBox')
$chatTitleBar = $window.FindName('ChatTitleBar'); $phaseHost = $window.FindName('PhaseHost')

$script:Endpoint = "http://127.0.0.1:1235"
$script:MaxTk = 1024; $script:Conversation = @()
$script:ChatStore = @(); $script:CurrentChatId = $null
$script:SessionDb = $null; $script:CurrentUser = $null; $script:CurrentSession = $null
$script:SearchEngine = $null; $sidebarOpen = $false
$script:MicroDir = Join-Path $PSScriptRoot ".learning\micronauts"
if (-not (Test-Path $script:MicroDir)) { New-Item -ItemType Directory -Path $script:MicroDir -Force | Out-Null }
$script:CodeExts = @('.cs','.ps1','.py','.js','.ts','.rs','.go','.cpp','.h','.java','.kt','.sql','.xml','.json','.yaml','.toml','.md','.txt','.ini','.cfg','.csv')
$script:ImgExts = @('.png','.jpg','.jpeg','.gif','.bmp','.svg','.webp')
$script:Attachments = @()

# Bubbles

$sendBtn.Add_Click({
    $msg = $inputBox.Text.Trim(); if (-not $msg) { return }; $inputBox.Text = ""
    if (Invoke-FluxCommand $msg) { return }
    Add-User $msg; $script:Conversation += @{ role = "user"; content = $msg; tick = $null }
    # Route through XCFE runtime
    try {
        if (-not $script:XCFERuntime) {
            $script:XCFERuntime = [NeuralGrammar.Core.XCFERuntime]::new()
            if ($script:MicronautRegister) { $script:XCFERuntime.MicronautRegister = $script:MicronautRegister }
        }
        # Wire worker availability into the runtime inspector.
        if (-not $script:XCFERuntime.MicronautRuntime) { $script:XCFERuntime.MicronautRuntime = [NeuralGrammar.Core.XCFE.MicronautRuntime]::new($script:DataDir) }
        $avail = $script:XCFERuntime.MicronautRuntime.GetAvailability()
        $factoryStatus = if ($avail.Factory) { 'OK' } else { 'MISSING' }
        $stdioStatus = if ($avail.StdioWorker) { 'OK' } else { 'MISSING' }
        $httpStatus = if ($avail.HttpWorker) { 'OK' } else { 'MISSING' }
        Write-Console ("factory: " + $factoryStatus + " [" + $avail.FactoryPath + "]") "System"
        Write-Console ("stdio worker: " + $stdioStatus + " [" + $avail.StdioWorkerPath + "]") "System"
        Write-Console ("http worker: " + $httpStatus + " [" + $avail.HttpWorkerPath + "]") "System"

        # Wire shared singletons
        if (-not $script:MicronautManager) {
            $microDir = Join-Path $script:Root ".learning\micronauts"
            $script:MicronautManager = [NeuralGrammar.Core.MicronautManager]::new($microDir, [NeuralGrammar.Core.DefaultSemanticReshaper]::new())
            $script:MicronautManager.Register = $script:MicronautRegister
            $null = $script:MicronautManager.LoadToRegister()
        }
        if (-not $script:TensorEngine) {
            $script:TensorEngine = [NeuralGrammar.Core.SemanticTensorEngine]::new()
            $script:TensorEngine.MicronautRegister = $script:MicronautRegister
            $script:TensorEngine.SessionCache = [NeuralGrammar.Core.SessionCache]::new(50)
        }
        # MicronautStore.OnChange -> console
        try {
            $store = [NeuralGrammar.Core.MicronautStore]::Global
            $store.add_OnChange({
                Write-Console "store: $($args[0]) = $($args[1])" "System"
            })
        } catch { }
        $route = $script:XCFERuntime.RouteTurn($msg, 4)
        $script:TickCounter++
        $turnTick = $script:TickCounter
        # Per-turn metadata for chat bubble badge
        $script:TurnMeta = @{
            Tick = $turnTick
            Model = $script:ActiveModel
            Brain = $route.Brain
            Fold = if ($route -and $route.Fold) { $route.Fold } else { "Sek" }
            Intent = $route.Intent
            Confidence = $route.Confidence
            Sources = @{
                Web = $false
                Boss = $false
                Micronauts = $false
                Replay = $false
                MutationPlus = $false
                Cheese = $false
                Local = $true
            }
            MicronautActions = @()
            Timestamp = (Get-Date -Format 'HH:mm:ss')
        }
        Write-Console "route-trace: tick=$turnTick intent=$($route.Intent)" "System"
        if ($route.Success) { Write-Console "route-trace: building context, $($route.Memories.Count) memories" "System" }
        # 2a causal provenance: what the field ENDORSED (matched n-grams) vs what RESULTED (fold trajectory)
        $endorsedT = @()
        if ($route.MatchedNgrams) {
            foreach ($ng in $route.MatchedNgrams) {
                $tk = @(($ng -split '\s+') | Where-Object { $_ })
                if ($tk.Count -ge 2) { for ($i = 0; $i -lt $tk.Count - 1; $i++) { $endorsedT += ,@($tk[$i], $tk[$i+1]) } }
                elseif ($tk.Count -eq 1) { $endorsedT += ,@($route.Intent, $tk[0]) }
            }
        }
        $resultT = @(); $ft = @($route.FoldTrace)
        for ($i = 0; $i -lt $ft.Count - 1; $i++) { $resultT += ,@($ft[$i], $ft[$i+1]) }
        $script:ExecutionTraces[$turnTick] = [PSCustomObject]@{
            Tick = $turnTick
            Text = $msg
            FoldTrace = @($route.FoldTrace)
            Intent = $route.Intent
            Brain = $route.Brain
            Confidence = $route.Confidence
            MemoryCount = $route.Memories.Count
            Memories = $route.Memories
            EndorsedTransitions = $endorsedT
            ResultTransitions = $resultT
            Success = $route.Success
            Fallback = $route.Fallback
            FallbackReason = $route.FallbackReason
            Timestamp = (Get-Date -Format 'O')
        }
        Write-Console "Tick ${turnTick}: $($route.Intent) -> $($route.Brain)" "Phase"
        if ($route.Success) {
            Write-Console "== Fold Trace ==" "Phase"
            foreach ($f in $route.FoldTrace) { Write-Console "  $f" "Phase" }
            Write-Console "Pop  retrieve candidate context" "Phase"
            Write-Console "Wo   bind subject + state" "Phase"
            Write-Console "Yax  classify: $($route.Intent) (conf=$($route.Confidence))" "Phase"
            Write-Console "Sek  route to: $($route.Brain)" "Phase"
            Write-Console "Chen assemble response" "Phase"
            Write-Console "Xul  collapse" "Phase"
            if ($route.Fallback) { Write-Console "fallback: $($route.FallbackReason)" "System" }
            $memCtx = @()
            $memIdx = 0
            foreach ($m in $route.Memories) {
                $memIdx++
                $md = $m.Data
                $subj = if ($md.subject) { $md.subject } else { "unknown" }
                $cap = if ($md.capability) { $md.capability } else { "generic" }
                $qual = if ($md.quality) { $md.quality } else { 0 }
                Write-Console "Candidate[$memIdx]: $subj ($cap) score=$($m.Score) quality=$qual" "Context"
                $memCtx += "[$($m.Node)] ${subj}: $cap (q=$qual)"
            }
            Write-Console "Model: $($script:ActiveModel) [$(if($script:ActiveModel -match 'gpt-oss|deepseek'){'BOSS'}else{'BASE'})]" "Model"
            Write-Console "Route: intent=$($route.Intent) brain=$($route.Brain) confidence=$($route.Confidence)" "System"
            # Inject fold state as context for the model
            $relevantMicros = Load-RelevantMicronauts $msg
            if ($relevantMicros.Count -gt 0) {
                $script:TurnMeta.Sources.Micronauts = $true
                $script:TurnMeta.Sources.Local = $false
            }
            $microContributions = @()
            foreach ($m in $relevantMicros) {
                Write-Console "discover: $($m.d.subject) [$($m.d.capability)]" "Tick"
                try { $contribution = Invoke-Micronaut $m $msg "Sek" } catch { Write-Console "invoke-fail: $($m.d.subject) $($_.Exception.Message)" "Error"; $contribution = $null }
                if ($contribution) {
                    Write-Console "execute: $($m.d.subject) (conf=$($contribution.Confidence))" "Tick"
                    # Route to real stdio/http worker if micronaut has opcodes or a worker URL.
                    $workerResult = Dispatch-MicronautWorker $m $msg $contribution
                    if ($workerResult) {
                        $contribution.Text = $workerResult.Text
                        $contribution.Confidence = $workerResult.Confidence
                        if ($workerResult.Evidence) { $contribution.Evidence = $workerResult.Evidence }
                        if ($workerResult.Transport) { $contribution | Add-Member -NotePropertyName 'Transport' -Value $workerResult.Transport -Force }
                    }
                    Submit-MicronautContribution $m $msg $contribution $turnTick
                    $microContributions += $contribution
                }
            }
            $contribCtx = if ($microContributions.Count -gt 0) { ($microContributions | ForEach-Object { "[$($_.Subject)] $($_.Text)" }) -join ' | ' } else { "" }
            # Provenance ledger: what actually executed vs what was discovered
            Write-Console "provenance: $($microContributions.Count) contributions, $($relevantMicros.Count) considered, intent=$($route.Intent)" "Tick"
            foreach ($c in $microContributions) {
                Write-Console "  contributor: $($c.Subject) [$($c.Capability)] conf=$($c.Confidence) fold=$($c.Fold)" "Tick"
            }
            $microCtx = if ($relevantMicros.Count -gt 0) { ($relevantMicros | ForEach-Object { "[$($_.d.subject)] $($_.d.capability)" }) -join ' ' } else { "no specific micronaut matched" }
            $ctxMsg = "Fold cycle: intent=$($route.Intent), brain=$($route.Brain), confidence=$($route.Confidence)`nActive micronauts: $microCtx"
            if ($memCtx.Count -gt 0) { $ctxMsg += "`nMemory: $($memCtx -join ' | ')" }
            $script:Conversation = @(@{ role = "system"; content = $ctxMsg }) + $script:Conversation
        }
    } catch { Write-Console "route: $($_.Exception.Message)" "Error" }
    # Load and execute matching K'UHUL program
    $programResult = $null
    $progFile = Join-Path $PSScriptRoot "schemas\programs\SemanticMemory.kuhul"
    $speculativePrograms = @(
        @{ pattern = '\b(reimagin|reinterpret|suppose|imagin|what.if|could.be|instead.of|non.human|alien.cognition|absurd|surreal|invent|creat|reject|redesign|alternativ|rewrite|rethink|fantasy|fiction|speculative|hypothetical)\b'; path = 'Sheogorath.kprog' }
    )
    if (Test-Path $progFile) {
        try { $programResult = Invoke-KuhulProgram $progFile $msg $turnTick } catch {
            Write-Console "program: $($_.Exception.Message)" "Error"
        }
    }
    foreach ($progSpec in $speculativePrograms) {
        if ($msg -match $progSpec.pattern) {
            $specPath = Join-Path $PSScriptRoot "schemas\programs\$($progSpec.path)"
            if (Test-Path $specPath) {
                try { $programResult = Invoke-KuhulProgram $specPath $msg $turnTick } catch {
                    Write-Console "program: $($_.Exception.Message)" "Error"
                }
            }
            break
        }
    }
    # Semantic page fault: unresolved turns must research and mint a micronaut
    # instead of letting the raw model take over the runtime.
    if (-not $script:TurnMeta.Sources.Micronauts -and ($route.Intent -eq 'unresolved' -or $route.Confidence -eq 0)) {
        Write-Console "semantic page fault: intent unresolved, researching..." "Phase"
        $researchText = Research-And-MintMicronaut $msg $route $turnTick
        if ($researchText) {
            # Re-run routing now that a provisional micronaut exists.
            # Limit to one re-route attempt to avoid loops if both web and
            # model-node sources are unavailable.
            $route = $script:XCFERuntime.RouteTurn($msg, 4)
            $script:TurnMeta.Brain = $route.Brain
            $script:TurnMeta.Fold = if ($route -and $route.Fold) { $route.Fold } else { "Sek" }
            $script:TurnMeta.Intent = $route.Intent
            $script:TurnMeta.Confidence = 0.50
            $script:TurnMeta.Sources.Micronauts = $true
            $script:TurnMeta.Sources.Local = $false
            # Seed the conversation with the research context as a system note
            $script:Conversation = @(@{ role = "system"; content = "Research context (provisional, confidence 0.50): $researchText" }) + $script:Conversation
            Write-Console "semantic page fault resolved: provisional micronaut registered" "Phase"
        } else {
            Write-Console "semantic page fault: research unavailable, proceeding with available context" "System"
            $script:TurnMeta.Sources.Local = $true
        }
    }
    $reply = $null
    for ($turn = 0; $turn -lt 5; $turn++) {
        $body = @{ messages = $script:Conversation; model = $script:ActiveModel; max_tokens = $script:MaxTk; temperature = 0.7; stream = $false } | ConvertTo-Json -Depth 4 -Compress
        try {
            $req = [System.Net.HttpWebRequest]::Create("$($script:Endpoint)/v1/chat/completions")
            $req.Method = "POST"; $req.ContentType = "application/json"; $req.Timeout = 300000
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body); $req.ContentLength = $bytes.Length
            $s = $req.GetRequestStream(); $s.Write($bytes,0,$bytes.Length); $s.Close()
            $rs = $req.GetResponse().GetResponseStream()
            $rd = New-Object System.IO.StreamReader($rs); $json = $rd.ReadToEnd(); $rd.Close()
            $resp = $json | ConvertFrom-JsonSafe
        } catch { Add-AI "Model error: $($_.Exception.Message)"; return }
        $mc = $resp.choices[0].message; $txt = $mc.content; $tc = $mc.tool_calls
        # Text with substance -> show and return
        if ($txt -and $txt.Trim().Length -gt 0 -and $txt -match '[a-zA-Z]{3,}') {
            $script:Conversation += @{ role = "assistant"; content = $txt; tick = $turnTick; meta = $script:TurnMeta }
            Add-AI $txt $script:TurnMeta
            Save-Chat

            # Route normal save vs. correction-driven refinement using conversation context
            $signal = Test-CorrectionSignal $msg
            $relevant = $null
            if ($script:LastMicronautId -and (Test-Path (Join-Path $script:MicroDir "$script:LastMicronautId.json"))) {
                $lastFile = Get-Item (Join-Path $script:MicroDir "$script:LastMicronautId.json")
                $relevant = @{ File = $lastFile; Data = (Get-Content $lastFile.FullName -Raw | ConvertFrom-JsonSafe); Score = 10 }
            }
            if (-not $relevant) { $relevant = Find-RelevantMicronaut $msg 6 }
            if ($signal -and $relevant) {
                $refined = Refine-Micronaut $relevant.File $txt $signal
                if ($refined) {
                    $script:TurnMeta.Sources.Local = $false
                    if (-not $script:TurnMeta.Sources.Boss) { $script:TurnMeta.Sources.Boss = $true }
                }
            } else {
                Save-Micronaut $msg $txt $(if ($route -and $route.Fold) { $route.Fold } else { "Sek" })
            }

            Write-Console "micronaut: $((($msg -split "\s+") | Where-Object { $_.Length -gt 4 } | Select-Object -First 2) -join "-") [$($(if ($route) { $route.Fold } else { "Sek" }))] conf=$($(if ($route) { $route.Confidence } else { "0.5" }))" "System"
            $fs = Get-ChildItem $script:MicroDir -Filter "*.json" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($fs -and $script:MicronautRegister) {
                try {
                    $json = Get-Content $fs.FullName -Raw | ConvertFrom-JsonSafe
                    $term = ($txt -split '\s+') | Where-Object { $_.Length -gt 4 } | Select-Object -First 2
                    $subj = if ($term) { ($term -join '-').ToLower() } else { "chat-response" }
                    $mn = [NeuralGrammar.Core.MicronautNode]::new()
                    $mn.GetType().GetProperty("Id").SetValue($mn, "auto_" + [System.Guid]::NewGuid().ToString("N").Substring(0,12))
                    $mn.GetType().GetProperty("Subject").SetValue($mn, $subj)
                    $mn.GetType().GetProperty("Capability").SetValue($mn, "chat")
                    $mn.GetType().GetProperty("Phase").SetValue($mn, [NeuralGrammar.Core.FoldPhase]::Sek)
                    $mn.GetType().GetProperty("Source").SetValue($mn, "chat")
                    $script:MicronautRegister.Register($mn)
                    Write-Console "register: updated" "Phase"
                } catch {}
            }
            return
        }
        # Tool calls -> execute and loop
        if ($tc -or $txt -match '"(?:name|function)":') {
            $fn = if ($tc) { $tc[0].function.name } else { $null }
            if (-not $fn) { $fn = [regex]::Match($txt, '"(?:name|function)":\s*"(\w+)"').Groups[1].Value }
            if (-not $fn) { $fn = "web_search" }
            Write-Console "tool: $fn" "ToolCall"
            $asm = @{ role = "assistant"; content = ""; tool_calls = @() }; $tmsgs = @()
            $tid = "tc_$(Get-Random)"
            $asm.tool_calls += @{ id = $tid; type = "function"; function = @{ name = $fn; arguments = "{}" } }
            $toolResult = ""
            switch -Wildcard ($fn) {
                "web_search" { try { $script:TurnMeta.Sources.Web = $true; $script:TurnMeta.Sources.Local = $false; $sw = New-Object System.Net.WebClient; $sw.Encoding = [System.Text.Encoding]::UTF8; $sr = $sw.DownloadString("https://api.duckduckgo.com/?q=$([System.Uri]::EscapeDataString($msg))&format=json&no_html=1"); $so = $sr | ConvertFrom-JsonSafe; $toolResult = if ($so.AbstractText) { $so.AbstractText } else { "No results" } } catch { $toolResult = "search error: $($_.Exception.Message)" } }
                "calculate" { try { $ce = $msg -replace '[^0-9+\-*/().%\s]',''; $toolResult = "Result: $(Invoke-Expression $ce -ErrorAction Stop)" } catch { $toolResult = "calc error" } }                
                "fetch_url" { try { $script:TurnMeta.Sources.Web = $true; $script:TurnMeta.Sources.Local = $false; $fw = New-Object System.Net.WebClient; $fw.Encoding = [System.Text.Encoding]::UTF8; $toolResult = $fw.DownloadString("$($msg)").Substring(0,[Math]::Min(2000, $($fw.DownloadString("$($msg)")).Length)) } catch { $toolResult = "fetch error: $($_.Exception.Message)" } }
                "file_io" { try { $fp = Join-Path $script:DataDir "tools\$(Get-Random).txt"; Set-Content $fp $msg -Encoding UTF8; $toolResult = "Saved to $fp" } catch { $toolResult = "file error" } }
                "advise" { try { $script:TurnMeta.Sources.Boss = $true; $script:TurnMeta.Sources.Local = $false; $toolResult = Invoke-Advise $msg } catch { $toolResult = "advise error: $($_.Exception.Message)" } }
                default { $toolResult = "Tool $fn executed successfully" }
            }
            $tmsgs += @{ role = "tool"; tool_call_id = $tid; content = $toolResult }
            Write-Console "tool result: $($toolResult.Substring(0,[Math]::Min(80,$toolResult.Length)))" "ToolResult"
            $script:Conversation += $asm; $script:Conversation += $tmsgs
        } else {
            $script:Conversation += @{ role = "assistant"; content = if ($txt) { $txt } else { "(empty)" }; tick = $turnTick; meta = $script:TurnMeta }; Add-AI $txt $script:TurnMeta; Save-Chat; return
        }
    }
    if (-not $reply) { Add-AI "(tool loop limit reached)" }
    Save-Chat; Update-MicronautLinks
})

$inputBox.Add_KeyDown({ if ($_.Key -eq 'Return') { $sendBtn.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent,$sendBtn)); $_.Handled = $true } })
$btnClose.Add_Click({ Save-Chat; Stop-PyProcesses; $window.Close() })
$btnShowSidebar.Add_Click({ $sidebarOpen = -not $sidebarOpen; $sidebarPanel.Visibility = if ($sidebarOpen) { "Visible" } else { "Collapsed" }; if ($sidebarOpen) { Refresh-ChatList } })
$btnToggleSidebar.Add_Click({ $sidebarOpen = $false; $sidebarPanel.Visibility = "Collapsed" })
$btnNewChat.Add_Click({ New-Chat })
if ($profileBox) { $profileBox.Add_MouseDown({ if ($script:CurrentUser) { Show-ProfileSettings } else { Show-LoginDialog } }) }
$btnUser.Add_Click({ if ($script:CurrentUser) { Show-ProfileSettings } else { Show-LoginDialog } })
if (Restore-Session) { Update-ProfileUI }

$btnSvg = $window.FindName('BtnSvg')

function Show-SvgVisualizer {
    try {
        # Create the modal window
        $dlg = New-Object Windows.Window
        $dlg.Title = "Phase Manifold"
        $dlg.Width = 700
        $dlg.Height = 650
        $dlg.WindowStartupLocation = "CenterOwner"
        $dlg.Owner = $window
        $dlg.Background = "#0d1117"
        
        # Create WebBrowser control
        $wb = New-Object System.Windows.Controls.WebBrowser
        
        # Find the HTML file
        $htmlPath = Join-Path $PSScriptRoot "phase-manifold.html"
        if (-not (Test-Path $htmlPath)) {
            $htmlPath = Join-Path (Get-Location) "phase-manifold.html"
        }
        
        if (Test-Path $htmlPath) {
            # Read HTML content
            $html = Get-Content $htmlPath -Raw
            
            # Add WebBrowser to window
            $dlg.Content = $wb
            
            # Navigate when window loads
            $dlg.Add_Loaded({
                # Use $wb (the WebBrowser control) not $this.Content
                $wb.NavigateToString($html)
            })
            
            Show-ThemedDialog $dlg
        }
        else {
            [Windows.MessageBox]::Show("phase-manifold.html not found", "SVG Error", "OK", "Error")
        }
    }
    catch {
        [Windows.MessageBox]::Show($_.Exception.Message, "SVG Error", "OK", "Error")
    }
}

$btnWizard = $window.FindName('BtnWizard')
function Show-WizardDialog {
    $wizDir = Join-Path $PSScriptRoot "projects"; if (-not (Test-Path $wizDir)) { New-Item -ItemType Directory -Path $wizDir -Force | Out-Null }
    $dlg = New-Object Windows.Window; $dlg.Title = "K'UHUL SHAMAN"; $dlg.Width = 680; $dlg.Height = 560
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window; $dlg.Background = '#000000'; $dlg.Foreground = '#ffffff'; $dlg.FontFamily = "Consolas"
    $g = [Windows.Controls.Grid]::new(); $g.Margin = '12'
    for ($i=0;$i-lt6;$i++) { $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)); $g.RowDefinitions[$i].Height = [Windows.GridLength]::new(28) }
    $g.RowDefinitions[0].Height = [Windows.GridLength]::new(36)
    $g.RowDefinitions[2].Height = [Windows.GridLength]::new(1, [Windows.GridUnitType]::Star)
    $g.RowDefinitions[4].Height = [Windows.GridLength]::new(80)
    $g.RowDefinitions[5].Height = [Windows.GridLength]::new(40)
    $title = New-Object Windows.Controls.TextBlock; $title.Text = "K'UHUL SHAMAN -- Program Forge"; $title.FontSize = 14; $title.FontWeight = "Bold"; $title.Foreground = '#58a6ff'
    [Windows.Controls.Grid]::SetRow($title,0); $g.Children.Add($title)
    $cb = New-Object Windows.Controls.ComboBox; $cb.Background = '#000000'; $cb.Foreground = '#ffffff'; $cb.BorderBrush = '#30363d'; $cb.FontSize = 12
    $cb.ItemContainerStyle = $itemStyle
    @("Kuhul Program (.kuhul)","Kuhul Micronaut (.kuhul + manifest)","Kuhul App PWA (index.html + manifest.kuhul + sw.khl)") | ForEach-Object { $cb.Items.Add($_) }; $cb.SelectedIndex = 0
    [Windows.Controls.Grid]::SetRow($cb,1); $g.Children.Add($cb)
    $nameBox = New-Object Windows.Controls.TextBox; $nameBox.Text = "MyProject"; $nameBox.Background = '#000000'; $nameBox.Foreground = '#ffffff'; $nameBox.BorderBrush = '#30363d'; $nameBox.FontSize = 12
    [Windows.Controls.Grid]::SetRow($nameBox,2); $g.Children.Add($nameBox)
    $descBox = New-Object Windows.Controls.TextBox; $descBox.Text = "Describe what this program does..."; $descBox.Background = '#000000'; $descBox.Foreground = '#ffffff'; $descBox.BorderBrush = '#30363d'; $descBox.FontSize = 12
    $descBox.TextWrapping = "Wrap"; $descBox.AcceptsReturn = $true; $descBox.VerticalScrollBarVisibility = "Auto"
    [Windows.Controls.Grid]::SetRow($descBox,3); $g.Children.Add($descBox)
    $logBox = New-Object Windows.Controls.TextBox; $logBox.IsReadOnly = $true; $logBox.Text = ""; $logBox.Background = '#000000'; $logBox.Foreground = '#8b949e'; $logBox.BorderBrush = '#30363d'; $logBox.FontSize = 11; $logBox.VerticalScrollBarVisibility = "Auto"
    [Windows.Controls.Grid]::SetRow($logBox,4); $g.Children.Add($logBox)
    $genBtn = New-Object Windows.Controls.Button; $genBtn.Content = "Generate with AI"; $genBtn.Background = '#3b82f6'; $genBtn.Foreground = 'White'; $genBtn.FontWeight = "Bold"; $genBtn.Width = 140; $genBtn.Height = 30; $genBtn.Margin = '2,0'
    $compileBtn = New-Object Windows.Controls.Button; $compileBtn.Content = "Compile with C#"; $compileBtn.Background = '#1f6feb'; $compileBtn.Foreground = 'White'; $compileBtn.FontWeight = "Bold"; $compileBtn.Width = 140; $compileBtn.Height = 30; $compileBtn.Margin = '2,0'
    $viewBtn = New-Object Windows.Controls.Button; $viewBtn.Content = "View HTML"; $viewBtn.Background = '#238636'; $viewBtn.Foreground = 'White'; $viewBtn.FontWeight = "Bold"; $viewBtn.Width = 100; $viewBtn.Height = 30; $viewBtn.Margin = '2,0'
    $compileBtn.Add_Click({
        $sel = $cb.SelectedItem; $name = $nameBox.Text
        $logBox.Text = "Compiling $name with C# MicronautWizard...`n"
        try {
            $tplMap = @{ "Kuhul Program (.kuhul)" = "Custom"; "Kuhul Micronaut (.kuhul + manifest)" = "Custom"; "Kuhul App PWA (index.html + manifest.kuhul + sw.khl)" = "Custom" }
            $tpl = "Custom"
            $src = [NeuralGrammar.Core.MicronautWizard]::Scaffold((Join-Path $PSScriptRoot "schemas\programs"), $tpl, $name)
            $logBox.Text += "Source: $src`n"
            $reg = if ([NeuralGrammar.Core.MicronautRegister]) { [NeuralGrammar.Core.MicronautRegister]::new() } else { $null }
            $result = [NeuralGrammar.Core.MicronautWizard]::Compile($src, $reg)
            if ($result.Success) {
                $logBox.Text += "SUCCESS: $($result.KprogPath)`nClosed-loop: $($result.IsClosedLoop)`nNodes: $($result.InstalledNodeCount)`n"
                $html = [NeuralGrammar.Core.HtmlViewer]::WrapProgram($src)
                $htmlPath = [System.IO.Path]::ChangeExtension($src, ".html")
                Set-Content $htmlPath $html -Encoding UTF8
                $logBox.Text += "HTML: $htmlPath`n"
                $script:LastShamanHtml = $htmlPath
            } else {
                $logBox.Text += "FAILED: $($result.Error)`n"
            }
        } catch { $logBox.Text += "ERROR: $($_.Exception.Message)`n" }
    })
    $genBtn.Add_Click({
        $sel = $cb.SelectedItem; $name = $nameBox.Text; $desc = $descBox.Text
        $logBox.Text = "Loading examples..."
        $examples = @(); $progDir = Join-Path $PSScriptRoot "schemas\programs"
        if (Test-Path $progDir) { foreach ($f in Get-ChildItem $progDir -Filter "*.kuhul" | Sort-Object Name) { $c = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue; if ($c) { $examples += "=== $($f.Name) ===`n$c" } } }
        $schemaDir = Join-Path $PSScriptRoot "schemas"; $grammar = ""
        if (Test-Path $schemaDir) { foreach ($f in Get-ChildItem $schemaDir -Filter "*.xsd") { $grammar += "File: $($f.Name)`n$(Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue | Select-Object -First 20)`n" } }
        $body = @{ messages = @(
            @{ role = "system"; content = "You are a KUHUL code generator. Output ONLY valid .kuhul code, no explanation." }
            @{ role = "user"; content = "Examples:`n$($examples -join "`n---`n")`n`nGrammar:$grammar" }
            @{ role = "user"; content = "Create a $sel named '$name'. $desc" }
        ); max_tokens = 2048; temperature = 0.3; stream = $false } | ConvertTo-Json -Depth 4 -Compress
        try {
            $req = [System.Net.HttpWebRequest]::Create("$($script:Endpoint)/v1/chat/completions")
            $req.Method = "POST"; $req.ContentType = "application/json"; $req.Timeout = 120000
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body); $req.ContentLength = $bytes.Length
            $s = $req.GetRequestStream(); $s.Write($bytes,0,$bytes.Length); $s.Close()
            $rs = $req.GetResponse().GetResponseStream()
            $rd = New-Object System.IO.StreamReader($rs); $json = $rd.ReadToEnd(); $rd.Close()
            $resp = $json | ConvertFrom-JsonSafe; $code = $resp.choices[0].message.content
            if ($code -match '```(?:kuhul)?\s*\n([\s\S]*?)\n\s*```') { $code = $matches[1] }
            $projDir = Join-Path $wizDir $name; New-Item -ItemType Directory -Path $projDir -Force | Out-Null
            Set-Content (Join-Path $projDir "$name.kuhul") $code -Encoding UTF8
            if ($sel -match "PWA") {
                # Inject theme into generated PWA
                $t = Get-Theme
                $html = '<!DOCTYPE html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><link rel="manifest" href="manifest.kuhul"><title>' + $name + '</title>'
                $html += '<style>'
                $html += ':root { --bg: ' + $t.window + '; --panel: ' + $t.panel + '; --text: ' + $t.text + '; --muted: ' + $t.text_muted + '; --accent: ' + $t.accent + '; --border: ' + $t.border + '; --input: ' + $t.input + '; }'
                $html += 'body { background:var(--bg); color:var(--text); font-family:Consolas,monospace; margin:0; padding:16px; }'
                $html += 'h1 { color:var(--accent); font-size:16px; font-weight:bold; }'
                $html += '#fold-trace { font:11px monospace; color:var(--muted); margin:12px 0; padding:8px; background:var(--panel); border:1px solid var(--border); border-radius:4px; }'
                $html += '#output { color:var(--text); font-size:12px; }'
                $html += '</style>'
                $html += '<script src="kuhul-runtime.js"></script></head><body>'
                $html += '<h1>' + $name + '</h1>'
                $html += '<div id="fold-trace"></div>'
                $html += '<div id="output"></div>'
                $html += '<script>if (window.KuhulRuntime) { KuhulRuntime.load("' + $name + '.kuhul").then(function(p) {'
                $html += '  document.getElementById("fold-trace").textContent = "Loaded: " + (p.meta ? p.meta.name : p.type);'
                $html += '}); }</script>'
                $html += '</body></html>'
                Set-Content (Join-Path $projDir "index.html") $html -Encoding UTF8
                Set-Content (Join-Path $projDir "manifest.kuhul") '{"name":"' + $name + '","short_name":"' + $name.substring(0,[Math]::Min(12,$name.Length)) + '","start_url":"index.html","display":"standalone","background_color":"#0d1117","theme_color":"#58a6ff","icons":[]}' -Encoding UTF8
                Set-Content (Join-Path $projDir "sw.khl") 'self.addEventListener("fetch", function(e) { e.respondWith(fetch(e.request).catch(function() { return new Response("K\"UHUL offline", {status:200}); })); });' -Encoding UTF8
                $logBox.Text += "`nPWA + runtime created."
            }
            $logBox.Text += "`nProject saved: $projDir"
            [Windows.Forms.MessageBox]::Show("Created: $projDir", "Wizard") | Out-Null
        } catch { $logBox.Text += "`nERROR: $($_.Exception.Message)"; [Windows.Forms.MessageBox]::Show($_.Exception.Message, "Error") | Out-Null }
    })
    $viewBtn.Add_Click({ try { if ($script:LastShamanHtml -and (Test-Path $script:LastShamanHtml)) { $vdlg = New-Object Windows.Window; $vdlg.Title = "K'UHUL Program"; $vdlg.Width = 750; $vdlg.Height = 700; $vdlg.WindowStartupLocation = "CenterOwner"; $vdlg.Owner = $window; $vdlg.Background = "#0d1117"; $vwb = New-Object System.Windows.Controls.WebBrowser; $vhtml = Get-Content $script:LastShamanHtml -Raw; $vdlg.Content = $vwb; $vdlg.Add_Loaded({ $vwb.NavigateToString($vhtml) }); Set-ControlTheme $vdlg; $null = $vdlg.ShowDialog() } else { [Windows.MessageBox]::Show("No compiled program yet", "View") | Out-Null } } catch { [Windows.MessageBox]::Show($_.Exception.Message, "View Error") | Out-Null } })
    $bp = [Windows.Controls.StackPanel]::new(); $bp.Orientation = "Horizontal"; $bp.Children.Add($genBtn); $bp.Children.Add($compileBtn); $bp.Children.Add($viewBtn)
    [Windows.Controls.Grid]::SetRow($bp,5); $g.Children.Add($bp)
    $dlg.Content = $g; Show-ThemedDialog $dlg
}
if ($btnWizard) { $btnWizard.Add_Click({ Show-WizardDialog }) }

Close-Splash $script:SplashCtx
Add-Sys "Micronaut Chat: ready"
Write-Console "K'UHUL runtime initialized" "System"
Write-Console "Models: LFM (BASE) / GPT-OSS (BOSS)" "Model"
Write-Console "Register: wait for micronauts" "Phase"
$btnWizard.Add_Click({ Show-WizardDialog })
$btnSvg.Add_Click({ Show-SvgVisualizer })
$btnInspector = $window.FindName('BtnInspector')
if ($btnInspector) { $btnInspector.Add_Click({ Show-RuntimeInspector }) }
# ============================================================================
# Splash Screen
# ============================================================================

# Boot splash



# ============================================================================
# Runtime Inspector (Modal)
# ============================================================================

function Get-MicronautState($filter) {
    if (-not (Test-Path $script:MicroDir)) { return @() }
    $result = @()
    foreach ($f in Get-ChildItem $script:MicroDir -Filter "*.json") {
        try {
            $d = Get-Content $f.FullName -Raw | ConvertFrom-JsonSafe
            $cap = $d.capability
            if ($filter -and $cap -ne $filter) { continue }
            $result += [PSCustomObject]@{
                Subject = $d.subject
                Capability = $cap
                Kind = if ($d.type -eq "executable" -or $cap -eq "eliza") { "worker" } else { "knowledge" }
                Executions = [int]($null -eq $d.executeCount ? 0 : $d.executeCount)
                Contributions = [int]($null -eq $d.contributionCount ? 0 : $d.contributionCount)
                LoadCount = [int]($null -eq $d.loadCount ? 0 : $d.loadCount)
                LastTick = $d.lastTick
                Confidence = $d.confidence
                ResponseLength = ($d.response | Measure-Object -Character).Characters
            }
        } catch { }
    }
    return $result | Sort-Object Contributions -Descending
}
function Show-RuntimeInspector {
    $dlg = New-Object Windows.Window
    $dlg.Title = "@flux — Runtime Inspector"; $dlg.Width = 720; $dlg.Height = 520
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window
    $dlg.Background = '#0d1117'; $dlg.Foreground = '#e2e8f0'; $dlg.FontFamily = "Consolas"; $dlg.FontSize = 12
    $g = [Windows.Controls.Grid]::new(); $g.Margin = '14'
    for ($i=0;$i-lt5;$i++) { $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)) }
    $g.RowDefinitions[0].Height = [Windows.GridLength]::new(70)
    $g.RowDefinitions[1].Height = [Windows.GridLength]::new(140)
    $g.RowDefinitions[2].Height = [Windows.GridLength]::new(1, [Windows.GridUnitType]::Star)
    $g.RowDefinitions[3].Height = [Windows.GridLength]::new(50)
    $g.RowDefinitions[4].Height = [Windows.GridLength]::new(40)

    $rt = $script:XCFERuntime
    $snap = if ($rt -and $rt.ExecState) { $rt.ExecState.GetSnapshot() } else { $null }

    # Row 0: Session header
    $headerPanel = [Windows.Controls.StackPanel]::new()
    $tickCount = if ($snap) { $snap.TotalTurns } else { 0 }
    $titleLine = New-Object Windows.Controls.TextBlock
    $titleLine.Text = "Session    XCFE Connected    Tick $tickCount"
    $titleLine.FontSize = 14; $titleLine.FontWeight = "Bold"; $titleLine.Foreground = '#58a6ff'
    $headerPanel.Children.Add($titleLine)
    $infoLine = New-Object Windows.Controls.TextBlock
    $infoLine.Text = "Project: NNC-K    Model: $($script:ActiveModel)"
    $infoLine.FontSize = 11; $infoLine.Foreground = '#8b949e'; $infoLine.Margin = '0,4,0,0'
    $headerPanel.Children.Add($infoLine)
    [Windows.Controls.Grid]::SetRow($headerPanel, 0); $g.Children.Add($headerPanel)

    # Row 1: Fold Timeline
    $foldPanel = [Windows.Controls.StackPanel]::new()
    $foldTitle = New-Object Windows.Controls.TextBlock
    $foldTitle.Text = "Fold Timeline"; $foldTitle.FontSize = 11; $foldTitle.FontWeight = "Bold"; $foldTitle.Foreground = '#f0883e'; $foldTitle.Margin = '0,0,0,4'
    $foldPanel.Children.Add($foldTitle)
    $folds = @("Pop","Wo","Yax","Sek","Ch'en","Xul")
    $foldCounts = @{}
    if ($snap) {
        $foldCounts["Pop"] = $snap.PopSteps; $foldCounts["Wo"] = $snap.WoSteps
        $foldCounts["Yax"] = $snap.YaxSteps; $foldCounts["Sek"] = $snap.SekSteps
        $foldCounts["Ch'en"] = $snap.ChenSteps; $foldCounts["Xul"] = $snap.XulSteps
    }
    $foldGrid = [Windows.Controls.WrapPanel]::new()
    foreach ($f in $folds) {
        $count = if ($foldCounts.ContainsKey($f)) { $foldCounts[$f] } else { 0 }
        $fb = New-Object Windows.Controls.Border
        $fb.Margin = '0,0,6,4'; $fb.Padding = '10,4'; $fb.CornerRadius = '4'
        $fb.Background = if ($count -gt 0) { '#1f6feb' } else { '#21262d' }
        $fb.BorderBrush = if ($count -gt 0) { '#58a6ff' } else { '#30363d' }
        $fb.BorderThickness = '1'
        $ft = New-Object Windows.Controls.TextBlock
        $ft.Text = " $f ($count)"; $ft.FontSize = 11; $ft.Foreground = if ($count -gt 0) { '#ffffff' } else { '#8b949e' }
        $fb.Child = $ft; $foldGrid.Children.Add($fb)
    }
    $foldPanel.Children.Add($foldGrid)
    [Windows.Controls.Grid]::SetRow($foldPanel, 1); $g.Children.Add($foldPanel)

    # Row 2: Micronauts + Memory stats + @flux summary
    $bodyGrid = [Windows.Controls.Grid]::new()
    $bodyGrid.ColumnDefinitions.Add((New-Object Windows.Controls.ColumnDefinition))
    $bodyGrid.ColumnDefinitions.Add((New-Object Windows.Controls.ColumnDefinition))
    $bodyGrid.ColumnDefinitions.Add((New-Object Windows.Controls.ColumnDefinition))
    $bodyGrid.ColumnDefinitions[0].Width = [Windows.GridLength]::new(1, [Windows.GridUnitType]::Star)
    $bodyGrid.ColumnDefinitions[1].Width = [Windows.GridLength]::new(180)
    $bodyGrid.ColumnDefinitions[2].Width = [Windows.GridLength]::new(160)

    $microPanel = [Windows.Controls.StackPanel]::new(); $microPanel.Margin = '0,0,8,0'
    $regCount = if ($script:MicronautRegister) { $script:MicronautRegister.Count } else { 0 }
    $microTitle = New-Object Windows.Controls.TextBlock
    $microTitle.Text = "Micronauts ($regCount)"; $microTitle.FontSize = 11; $microTitle.FontWeight = "Bold"; $microTitle.Foreground = '#d2a8ff'; $microTitle.Margin = '0,0,0,4'
    $microPanel.Children.Add($microTitle)
    if ($script:MicronautRegister) {
        $shown = 0
        foreach ($n in $script:MicronautRegister.All) {
            if ($shown -ge 8) { break }
            $state = if ($n.IsDaemon) { "DAEMON" } else { "IDLE" }
            $ml = New-Object Windows.Controls.TextBlock
            $ml.Text = "  $($n.Subject)  $state"; $ml.FontSize = 10; $ml.Foreground = '#c9d1d9'; $ml.Margin = '0,1,0,0'
            $microPanel.Children.Add($ml); $shown++
        }
    }
    [Windows.Controls.Grid]::SetColumn($microPanel, 0); $bodyGrid.Children.Add($microPanel)

    $memPanel = [Windows.Controls.StackPanel]::new()
    $memTitle = New-Object Windows.Controls.TextBlock
    $memTitle.Text = "Memory"; $memTitle.FontSize = 11; $memTitle.FontWeight = "Bold"; $memTitle.Foreground = '#3fb950'; $memTitle.Margin = '0,0,0,4'
    $memPanel.Children.Add($memTitle)
    $retrieved = if ($snap) { $snap.SuccessfulTurns } else { 0 }
    $foldCov = if ($snap) { "{0:P1}" -f $snap.FoldCoverage } else { "0%" }
    $succRate = if ($snap) { "{0:P1}" -f $snap.SuccessRate } else { "0%" }
    $avgConf = if ($snap) { "{0:N2}" -f $snap.AverageConfidence } else { "0.00" }
    $memRecall = if ($snap) { "{0:P1}" -f $snap.MemoryRecallRate } else { "0%" }
    foreach ($s in @("Retrieved: $retrieved","Fold Coverage: $foldCov","Success Rate: $succRate","Confidence: $avgConf","Memory Recall: $memRecall")) {
        $sl = New-Object Windows.Controls.TextBlock; $sl.Text = "  $s"; $sl.FontSize = 10; $sl.Foreground = '#8b949e'; $sl.Margin = '0,2,0,0'
        $memPanel.Children.Add($sl)
    }
    [Windows.Controls.Grid]::SetColumn($memPanel, 1); $bodyGrid.Children.Add($memPanel)

    # Runtime worker transport availability panel
    $workerPanel = [Windows.Controls.StackPanel]::new()
    $workerTitle = New-Object Windows.Controls.TextBlock
    $workerTitle.Text = "Worker Transports"; $workerTitle.FontSize = 11; $workerTitle.FontWeight = "Bold"; $workerTitle.Foreground = '#8b949e'; $workerTitle.Margin = '0,0,0,4'
    $workerPanel.Children.Add($workerTitle)
    $avail = if ($script:XCFERuntime -and $script:XCFERuntime.MicronautRuntime) { $script:XCFERuntime.MicronautRuntime.GetAvailability() } else { $null }
    foreach ($pair in @(
        @('factory', ($avail -ne $null -and $avail.Factory), ($avail -ne $null ? $avail.FactoryPath : '?')),
        @('stdio', ($avail -ne $null -and $avail.StdioWorker), ($avail -ne $null ? $avail.StdioWorkerPath : '?')),
        @('http', ($avail -ne $null -and $avail.HttpWorker), ($avail -ne $null ? $avail.HttpWorkerPath : '?'))
    )) {
        $wl = New-Object Windows.Controls.TextBlock
        $status = if ($pair[1]) { 'OK' } else { 'MISSING' }
        $wl.Text = "  $($pair[0]): $status"
        $wl.FontSize = 9; $wl.Foreground = if ($pair[1]) { '#3fb950' } else { '#f85149' }; $wl.Margin = '0,1,0,0'
        $wl.ToolTip = $pair[2]
        $workerPanel.Children.Add($wl)
    }
    [Windows.Controls.Grid]::SetColumn($workerPanel, 2); $bodyGrid.Children.Add($workerPanel)
    [Windows.Controls.Grid]::SetRow($bodyGrid, 2); $g.Children.Add($bodyGrid)

    # Row 3: Recent events
    $eventPanel = [Windows.Controls.StackPanel]::new()
    $eventTitle = New-Object Windows.Controls.TextBlock
    $eventTitle.Text = "Recent Events ($($script:EventRegister.Count))"; $eventTitle.FontSize = 11; $eventTitle.FontWeight = "Bold"; $eventTitle.Foreground = '#8b949e'; $eventTitle.Margin = '0,0,0,4'
    $eventPanel.Children.Add($eventTitle)
    foreach ($ev in $script:EventRegister | Select-Object -Last 5) {
        $el = New-Object Windows.Controls.TextBlock; $el.Text = "  #$($ev.Tick) [$($ev.Category)] $($ev.Message)"
        $el.FontSize = 9; $el.Foreground = '#585e6b'; $el.Margin = '0,1,0,0'
        $eventPanel.Children.Add($el)
    }
    [Windows.Controls.Grid]::SetRow($eventPanel, 3); $g.Children.Add($eventPanel)

    # Row 4: Action buttons
    $btnPanel = [Windows.Controls.StackPanel]::new(); $btnPanel.Orientation = "Horizontal"; $btnPanel.HorizontalAlignment = "Right"
    $exportBtn = New-Object Windows.Controls.Button
    $exportBtn.Content = "Export Trace"; $exportBtn.Background = '#1f6feb'; $exportBtn.Foreground = 'White'; $exportBtn.Height = 28; $exportBtn.Width = 100; $exportBtn.Margin = '0,0,6,0'
    $exportBtn.Add_Click({
        $traceDir = Join-Path $script:DataDir "traces"; if (-not (Test-Path $traceDir)) { New-Item -ItemType Directory -Path $traceDir -Force | Out-Null }
        $tracePath = Join-Path $traceDir "trace-$(Get-Date -Format 'yyyyMMddHHmmss').json"
        @{ ticks = $script:TickCounter; conversation = $script:Conversation; events = $script:EventRegister; traces = $script:ExecutionTraces } | ConvertTo-Json | Set-Content $tracePath -Encoding UTF8
        [Windows.MessageBox]::Show("Trace exported: $tracePath", "Export") | Out-Null
    })
    $btnPanel.Children.Add($exportBtn)
    $replayBtn = New-Object Windows.Controls.Button
    $replayBtn.Content = "Replay"; $replayBtn.Background = '#238636'; $replayBtn.Foreground = 'White'; $replayBtn.Height = 28; $replayBtn.Width = 80; $replayBtn.Margin = '0,0,6,0'
    $replayBtn.Add_Click({ Add-Sys "REPLAY: fold cycle replay started" })
    $btnPanel.Children.Add($replayBtn)
    $saveBtn = New-Object Windows.Controls.Button
    $saveBtn.Content = "Save"; $saveBtn.Background = '#21262d'; $saveBtn.Foreground = '#e2e8f0'; $saveBtn.Height = 28; $saveBtn.Width = 70; $saveBtn.Margin = '0,0,6,0'
    $saveBtn.Add_Click({ Save-Chat; $dlg.Close() })
    $btnPanel.Children.Add($saveBtn)
    $closeBtn = New-Object Windows.Controls.Button
    $closeBtn.Content = "Close"; $closeBtn.Background = '#21262d'; $closeBtn.Foreground = '#e2e8f0'; $closeBtn.Height = 28; $closeBtn.Width = 80
    $closeBtn.Add_Click({ $dlg.Close() })
    $btnPanel.Children.Add($closeBtn)
    [Windows.Controls.Grid]::SetRow($btnPanel, 4); $g.Children.Add($btnPanel)

    $dlg.Content = $g; Show-ThemedDialog $dlg
}


function Show-TickInspector($tick) {
    if (-not $script:ExecutionTraces.ContainsKey($tick)) {
        Add-Sys "Time Travel: no trace for tick #$tick"; return
    }
    $trace = $script:ExecutionTraces[$tick]
    $dlg = New-Object Windows.Window
    $dlg.Title = "Time Travel - Tick #$tick"; $dlg.Width = 520; $dlg.Height = 380
    $dlg.WindowStartupLocation = "CenterOwner"; $dlg.Owner = $window
    $dlg.Background = '#0d1117'; $dlg.Foreground = '#e2e8f0'; $dlg.FontFamily = "Consolas"; $dlg.FontSize = 12
    $g = [Windows.Controls.Grid]::new(); $g.Margin = '14'
    for ($i=0;$i-lt3;$i++) { $g.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition)) }
    $g.RowDefinitions[0].Height = [Windows.GridLength]::new(60)
    $g.RowDefinitions[1].Height = [Windows.GridLength]::new(1, [Windows.GridUnitType]::Star)
    $g.RowDefinitions[2].Height = [Windows.GridLength]::new(40)
    $header = New-Object Windows.Controls.TextBlock
    $header.Text = "Tick #$tick -- $($trace.Intent) -> $($trace.Brain) (conf=$($trace.Confidence))"
    $header.FontSize = 14; $header.FontWeight = "Bold"; $header.Foreground = '#58a6ff'
    [Windows.Controls.Grid]::SetRow($header, 0); $g.Children.Add($header)
    $bodyPanel = [Windows.Controls.StackPanel]::new()
    $ft = New-Object Windows.Controls.TextBlock; $ft.Text = "Fold Trace:"; $ft.FontSize = 11; $ft.FontWeight = "Bold"; $ft.Foreground = '#f0883e'
    $bodyPanel.Children.Add($ft)
    foreach ($f in $trace.FoldTrace) {
        $fl = New-Object Windows.Controls.TextBlock; $fl.Text = "  $f"; $fl.FontSize = 10; $fl.Foreground = '#58a6ff'; $fl.Margin = '0,1,0,0'
        $bodyPanel.Children.Add($fl)
    }
    $mt = New-Object Windows.Controls.TextBlock; $mt.Text = "Memories ($($trace.MemoryCount)):"; $mt.FontSize = 11; $mt.FontWeight = "Bold"; $mt.Foreground = '#3fb950'; $mt.Margin = '0,8,0,0'
    $bodyPanel.Children.Add($mt)
    foreach ($m in $trace.Memories) {
        $md = $m.Data; $subj = if ($md.subject) { $md.subject } else { "unknown" }; $cap = if ($md.capability) { $md.capability } else { "generic" }
        $ml = New-Object Windows.Controls.TextBlock; $ml.Text = "  $subj ($cap) score=$($m.Score)"; $ml.FontSize = 10; $ml.Foreground = '#8b949e'; $ml.Margin = '0,1,0,0'
        $bodyPanel.Children.Add($ml)
    }
    $st = New-Object Windows.Controls.TextBlock; $st.Text = "Status: $(if($trace.Success){'Success'}else{'Failed'})  Fallback: $(if($trace.Fallback){$trace.FallbackReason}else{'No'})"
    $st.FontSize = 10; $st.FontWeight = "Bold"; $st.Foreground = if ($trace.Success) { '#3fb950' } else { '#f85149' }; $st.Margin = '0,8,0,0'
    $bodyPanel.Children.Add($st)
    $scroll = New-Object Windows.Controls.ScrollViewer; $scroll.VerticalScrollBarVisibility = "Auto"; $scroll.Content = $bodyPanel
    [Windows.Controls.Grid]::SetRow($scroll, 1); $g.Children.Add($scroll)
    $closeBtn = New-Object Windows.Controls.Button; $closeBtn.Content = "Close"; $closeBtn.Background = '#21262d'; $closeBtn.Foreground = '#e2e8f0'; $closeBtn.Height = 28; $closeBtn.Width = 80; $closeBtn.HorizontalAlignment = "Right"
    $closeBtn.Add_Click({ $dlg.Close() })
    [Windows.Controls.Grid]::SetRow($closeBtn, 2); $g.Children.Add($closeBtn)
    $dlg.Content = $g; Show-ThemedDialog $dlg
}

$null = $window.ShowDialog()


