using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZapparCliTools
{
    // Runs any [CliCommand] of this project in batch mode, for when the Unity CLI isn't available
    // (tools/zappar.sh wraps it, with the project lock):
    //   -executeMethod ZapparCliTools.ZapparBatch.Run -zapparCommand zappar_add_target -image ~/poster.png
    // Every "-name value" pair after -zapparCommand fills the command argument with that name.
    // Prints "[ZAPPAR] ..." lines; exit code 1 when the command fails.
    public static class ZapparBatch
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-zapparCommand");
            if (at < 0 || at + 1 >= args.Length) { Fail("pass -zapparCommand <name>"); return; }
            string name = args[at + 1];

            MethodInfo method = null;
            foreach (var type in TypeCache.GetMethodsWithAttribute<Unity.Pipeline.Commands.CliCommandAttribute>())
                if (type.GetCustomAttribute<Unity.Pipeline.Commands.CliCommandAttribute>().Name == name) method = type;
            if (method == null)
            {
                var all = TypeCache.GetMethodsWithAttribute<Unity.Pipeline.Commands.CliCommandAttribute>()
                    .Select(m => m.GetCustomAttribute<Unity.Pipeline.Commands.CliCommandAttribute>().Name).Where(n => n.StartsWith("zappar_") || n.StartsWith("showcase_")).OrderBy(n => n);
                Fail("unknown command '" + name + "'. Known: " + string.Join(", ", all));
                return;
            }

            var parameters = method.GetParameters();
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var argAttr = p.GetCustomAttribute<Unity.Pipeline.Commands.CliArgAttribute>();
                string argName = argAttr != null ? argAttr.Name : p.Name;
                int idx = Array.IndexOf(args, "-" + argName, at);
                if (idx >= 0 && idx + 1 < args.Length) values[i] = Convert.ChangeType(args[idx + 1], p.ParameterType);
                else if (p.HasDefaultValue) values[i] = p.DefaultValue;
                else { Fail(name + " needs -" + argName); return; }
            }

            try
            {
                var result = method.Invoke(null, values);
                // One tag per line so log filters (tools/unity-batch.sh) keep the whole result.
                Debug.Log(string.Join("\n", (name + " OK\n" + result).Split('\n').Select(l => "[ZAPPAR] " + l)));
            }
            catch (TargetInvocationException e)
            {
                Fail(name + ": " + e.InnerException?.Message);
            }
        }

        static void Fail(string message)
        {
            Debug.LogError("[ZAPPAR] FAILED " + message);
            EditorApplication.Exit(1);
        }
    }
}
