# Sawyer's C# Templates

This repo contains templates for the .NET SDK that are more batteries-included,
subject to my preferences.

This is a companion to
[A Beginner's Guide to .NET's HostBuilder](https://medium.com/@sawyer.watts/a-beginners-guide-to-net-s-hostbuilder-part-0-78882aab60f8).

## Installing

Here is the Bash to build and install this template from source:

```sh
temp_dir=~/.sawyerCSharpTemplatesTemp
git clone https://github.com/sawyerwatts/SawyerCSharpTemplates $temp_dir
cd $temp_dir
dotnet pack -c Release -o out
dotnet new install out/*.nupkg
cd -
rm -rf $temp_dir
```

## Uninstalling

Here is the Bash to uninstall an existing template:

```sh
dotnet new uninstall SawyerCSharpTemplates
```

## Updating

Uninstall and reinstall the templates to update.

## Listing

To view the templates installed by this package, use `dotnet new list sawyer`.
