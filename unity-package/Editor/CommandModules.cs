using System;
using System.Collections.Generic;
using System.Reflection;

// Permite agregar comandos nuevos creando solo un archivo nuevo:
// cualquier método estático marcado con [Command("nombre")] queda disponible.
public static partial class BridgeServer
{
    [AttributeUsage(AttributeTargets.Method)]
    class CommandAttribute : Attribute
    {
        public readonly string Name;

        public CommandAttribute(string name)
        {
            Name = name;
        }
    }

    static Dictionary<string, MethodInfo> commandTable;

    static string ExecuteModules(string command, string body)
    {
        if (commandTable == null)
        {
            commandTable = new Dictionary<string, MethodInfo>();
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in typeof(BridgeServer).GetMethods(flags))
            {
                var attr = (CommandAttribute)Attribute.GetCustomAttribute(method, typeof(CommandAttribute));
                if (attr != null)
                    commandTable[attr.Name] = method;
            }
        }

        MethodInfo found;
        if (!commandTable.TryGetValue(command, out found))
            return Json(false, "Comando desconocido: " + command);

        try
        {
            return (string)found.Invoke(null, new object[] { body });
        }
        catch (TargetInvocationException e)
        {
            var inner = e.InnerException != null ? e.InnerException : e;
            return Json(false, inner.Message);
        }
    }
}