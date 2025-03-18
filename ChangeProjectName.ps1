# Define the root directory of your solution
$rootDir = (Get-Location).Path
$rootDirName = [System.IO.Path]::GetFileName($rootDir)

# Get the old project name from the .sln file
$slnFile = Get-ChildItem -Path $rootDir -Filter *.sln | Select-Object -First 1
if ($slnFile) {
    $oldProjectName = [System.IO.Path]::GetFileNameWithoutExtension($slnFile.Name)
} else {
    Write-Host "No .sln file found in the root directory. Exiting script."
    exit
}

# Prompt the user for the new project name with default value
$newProjectName = Read-Host "Enter the new project name (default: $rootDirName)"
if ([string]::IsNullOrWhiteSpace($newProjectName)) {
    $newProjectName = $rootDirName
}

# Function to display colored messages
function Show-ConfirmMessage {
    param (
        [string]$message
    )
    Write-Host $message -ForegroundColor Red
    return (Read-Host -Prompt "Enter 'y' to proceed or any other key to cancel")
}

# Confirm the operation
$confirm1 = Show-ConfirmMessage "You are about to replace all instances of '$oldProjectName' with '$newProjectName'. Do you want to proceed? (y/n)"
if ($confirm1 -ne "y") {
    Write-Host "Operation cancelled."
    exit
}

$confirm2 = Show-ConfirmMessage "Are you sure you want to proceed? This action cannot be undone. (y/n)"
if ($confirm2 -ne "y") {
    Write-Host "Operation cancelled."
    exit
}

# Function to replace text in files
function Replace-TextInFile {
    param (
        [string]$filePath,
        [string]$oldText,
        [string]$newText
    )
    # Read the content, replace the text, and write back with UTF8 encoding with BOM
    (Get-Content -Path $filePath -Raw) -replace [regex]::Escape($oldText), $newText | Set-Content -Path $filePath -Encoding UTF8
}

# Replace text in all relevant files
$files = Get-ChildItem -Path $rootDir -Recurse -Include *.cs, *.csproj, *.sln, *.config, *.json, *.razor, *.http, *.sh, *.js, *.css
foreach ($file in $files) {
    if ($file.FullName -ne $MyInvocation.MyCommand.Path) {
        Replace-TextInFile -filePath $file.FullName -oldText $oldProjectName -newText $newProjectName
    }
}

# Rename files
$files = Get-ChildItem -Path $rootDir -Recurse -File -Include *$oldProjectName*
foreach ($file in $files) {
    $newFileName = $file.Name -replace $oldProjectName, $newProjectName
    Rename-Item -Path $file.FullName -NewName $newFileName
}

# Rename directories
$directories = Get-ChildItem -Path $rootDir -Recurse -Directory -Include *$oldProjectName*
foreach ($directory in $directories) {
    $newDirectoryName = $directory.Name -replace $oldProjectName, $newProjectName
    Rename-Item -Path $directory.FullName -NewName $newDirectoryName
}

# Check for .git directory and prompt for removal
$gitDir = Join-Path -Path $rootDir -ChildPath ".git"
if (Test-Path -Path $gitDir) {
    $confirmRemoval1 = Show-ConfirmMessage "The .git directory exists. Do you want to remove it? (y/n)"
    if ($confirmRemoval1 -eq "y") {
        $confirmRemoval2 = Show-ConfirmMessage "Are you sure you want to remove the .git directory? This action cannot be undone. (y/n)"
        if ($confirmRemoval2 -eq "y") {
            Remove-Item -Path $gitDir -Recurse -Force
            Write-Host ".git directory has been removed."
        } else {
            Write-Host ".git directory has not been removed."
        }
    } else {
        Write-Host ".git directory has not been removed."
    }
}

Write-Host "Replacement complete. All instances of '$oldProjectName' have been replaced with '$newProjectName'."