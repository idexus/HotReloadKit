using System;
using HotReloadKit;

namespace HotReloadExample;

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
  
class A
{
 
}
 
class B
{     
      
} 
  
class C
{
  
} 