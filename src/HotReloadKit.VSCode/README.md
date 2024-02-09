# HotReloadKit

HotReloadKit is an extension designed for C# .NET Core projects, providing efficient hot reloading capabilities. Compatible with .NET MAUI projects, it's a crucial tool utilized by Sharp.UI, a library simplifying .NET MAUI development with fluent methods solely in C# code.

- [Sharp.UI GitHub Repository](https://github.com/idexus/Sharp.UI)

## Nuget package

Add the following NuGet package to your project:

- [https://www.nuget.org/packages/HotReloadKit](https://www.nuget.org/packages/HotReloadKit)

## Simple Usage 

```csharp
using HotReloadKit;
using System;

class Program
{
    static void Main(string[] args)
    {
        HotReloader.Init<Program>(HotReloadSupport.IdeIPs);
        HotReloader.RequestAdditionalTypes = () => new string[] { "HotReloadExample.MyClass" };        
        HotReloader.UpdateApplication = dataList =>
        {
            foreach (var data in dataList) 
                Console.WriteLine($"{data.Type.FullName} isFromChangedFile: {data.IsFromChangedFile}");
        };

        Console.ReadLine();
    }
} 
```

## For More Information

* [GitHub repository](https://github.com/idexus/HotReloadKit)

## Disclaimer

There is no official support. Use at your own risk.

## License

Licensed under the MIT License. © 2023 Pawel Krzywdzinski

**Enjoy!**
