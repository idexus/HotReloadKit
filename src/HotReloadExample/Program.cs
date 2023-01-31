using System;
using System.Net;
using System.Threading.Tasks;
using HotReloadKit;

namespace HotReloadExample;

class Program
{
    static void Main(string[] args)
    {
        CodeReloader.Init<Program>(HotReloadSupport.IdeIPs);
        CodeReloader.RequestedTypeNamesHandler = () =>
        {
            return new string[] { "HotReloadExample.MyClass" };
        };
        CodeReloader.UpdateApplication = types =>
        {
            foreach (var type in types)
                Console.WriteLine(type.FullName);
        };
        
        Console.WriteLine("Hot Reload Test!");
        Console.ReadLine();
    }
}
   
class Testss
{    
    
}  
   
class ABC 
{ 
      
}

class BC
{
 
}    