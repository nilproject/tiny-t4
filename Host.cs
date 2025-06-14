namespace tiny_t4;

public sealed class Host
{
    public string TemplateFile { get; }

    public Host(string templateFile)
    {
        TemplateFile = templateFile;
    }
}