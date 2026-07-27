namespace Harness.BusinessLogic.Framework;

public interface IFrameworkResolver
{
    FrameworkResolution Resolve(IReadOnlyList<FrameworkRule> rules);
}
