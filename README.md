# Frends.HIT.TaskTemplate
Template for creating tasks for the Frends integration platform

# Usage
Clone the template and change all occurences of "Frends.HIT.TaskTemplate" to "Frends.HIT.YourTaskName". Then, follow the steps below.

# Manually building
To manually build and upload a task, do the following

## Linux
```bash
#!/bin/bash

# 1. Get the version number from the VERSION file, or specify it manually. The VERSION file should always contains the last deployed version number.

## Automatically read and increment the version
IFS=. read -r v1 v2 v3 <<< $(cat ./VERSION)
v3=$((v3 + 1))
VERSION="$v1.$v2.$v3"

## Manually specify the version
VERSION="1.0.1"

# 2. Build and package the task
dotnet pack --configuration Release --include-source --output . /p:Version=$VERSION Frends.HIT.TaskTemplate/Frends.HIT.TaskTemplate.csproj

# 3. Upload the package to the nuget feed
dotnet nuget push ./Frends.HIT.TaskTemplate.$VERSION.nupkg --source "https://feed.addr.com/nuget/Feedname/" --api-key "YourAPIKeyForThePackageFeed"

# 4. If successful, update the version file with the new version number
echo "$VERSION" > VERSION
```

## Windows
```powershell
# 1. Get the current version number from the version file and increment it
## Automatically read and increment the version
$SplitVersion = $(Get-Content -Path ./Version).ToString().Split('.')
$SplitVersion[2] = $SplitVersion[2] + 1
$VERSION = $SplitVersion.Join('.')

## Manually specify the version
$VERSION = "1.0.1"

# 2. Build and package the task
dotnet pack --configuration Release --include-source --output . /p:Version="$VERSION" Frends.HIT.TaskTemplate/Frends.HIT.TaskTemplate.csproj

# 3. Upload the package to the nuget feed
dotnet nuget push ./Frends.HIT.TaskTemplate.$VERSION.nupkg --source "https://feed.addr.com/nuget/Feedname/" --api-key "YourAPIKeyForThePackageFeed"

# 4. If successful, update the version file with the new version number
Set-Content -Path ./VERSION -Value "$VERSION"
```

# Project Structure
## VERSION
Contains the current version of the task, automatically incremented on build

## Frends.HIT.TaskTemplate.csproj
The metadata for the task project.

## FrendsTaskMetadata.json
Contains the list of functions to expose to the Frends Integration Platform in Json format, e.g.
```json
{
    "Tasks": [
      {
        "TaskMethod": "Frends.HIT.TaskTemplate.Main.DoSomething"
      }
    ]
}
```

## Main.cs
Contains the main functions that are exposed to the Frends Integration Platform.

### Properties
#### [PropertyTab]
Shows this set of parameters as a separate tab in the interface instead of on the same page as the other attributes.

## Definitions.cs
Contains the definitions for the data models that are used in the task

### Types
#### Enum
An enum is used to display a dropdown list of options to the user for a given input.

### Properties
#### [Display(Name = "Display Name")]
The name for the option or input field that is displayed to the user

#### [DefaultValue("ABC")]
The default value for the input field. In case of Enums (i.e. MultipleOptionEnum), default is defined as ```DefaultValue(MultipleOptionEnum.OptionA)```

#### [PasswordPropertyText]
Define the field as a password, not revealing the input by default

#### [DisplayFormat(DataFormatString = "Text")]
Which type of input should be the default for the given option/parameter. Valid options are Text, Json, Expression, XML, SQL.

#### [UIHint(nameof(OtherField), "", "OptionA", "OptionB")]
Only show this field if the value of OtherField is "OptionA" or "OptionB"


### Helpers.cs
Contains helper functions to assist in doing various tasks

# Customizing the action workflow
The requirements for the workflow are the following

### VERSION file
There needs to be a file named VERSION in the root of the repository containing a 3-digit version number (e.g. 0.0.1). 
This will be automatically incremented for each build.

### Environment variables
|Name|Description| Value                                      |
|---|---|--------------------------------------------|
|NUGET_PKG_KEY|The NuGet feed package API key| your NuGet feed package API key|
|NUGET_PKG_NAME|The name of the NuGet package, defaults to repository name| Frends.HIT.TaskTemplate|
|NUGET_PKG_FEED|The URL of the nuget package feed| https://pkgrepo.somedomain.com/feeds/Frends/|
|GH_PUSH_EMAIL|The email address used for pushing the incremented version number back to the repository| autopush@developer.local                   |
|GH_PUSH_NAME|The name to use for pushing the incremented version number back to the repository| AutoPush                                   |

# Requirements
.NET Core 6.0