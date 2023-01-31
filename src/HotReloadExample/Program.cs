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
            return new string[] { "" };
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

class Test
{  
      
} 

class A
{ 
     
}

class B
{
 
} 