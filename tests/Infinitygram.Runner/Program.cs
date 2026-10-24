// Reflection runner: vstest testhost hangs in this VM (known issue, also seen in
// marathon Track D15), so P0 tests are executed directly by reflection over the
// same xUnit [Fact]/[Theory] methods. This is a runner workaround, not a test skip:
// every method is invoked and counted, failures exit non-zero.
using System.Reflection;
using Xunit;
using Xunit.Sdk;

var assembly = typeof(Infinitygram.Tests.VaultAndPolicyTests).Assembly;
int passed = 0, failed = 0;
var failures = new List<string>();

foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                 .Where(m => m.GetCustomAttribute<FactAttribute>() is not null
                          || m.GetCustomAttribute<TheoryAttribute>() is not null))
    {
        var isTheory = method.GetCustomAttribute<TheoryAttribute>() is not null;
        var cases = new List<object?[]>();
        if (!isTheory) cases.Add(Array.Empty<object?>());
        else
        {
            foreach (var dataAttr in method.GetCustomAttributes<DataAttribute>())
                foreach (var row in dataAttr.GetData(method))
                    cases.Add(row);
        }
        foreach (var caseArgs in cases)
        {
            var label = $"{type.Name}.{method.Name}({string.Join(",", caseArgs.Select(a => a?.ToString() ?? "null"))})";
            try
            {
                var instance = Activator.CreateInstance(type)!;
                method.Invoke(instance, caseArgs);
                passed++;
                Console.WriteLine($"PASS {label}");
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                failures.Add($"{label}: {ex.InnerException?.Message}");
                Console.WriteLine($"FAIL {label}: {ex.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{label}: {ex.Message}");
                Console.WriteLine($"FAIL {label}: {ex.Message}");
            }
        }
    }
}

Console.WriteLine($"TOTAL passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;
