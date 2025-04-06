using Antelcat.FlatBuffers;

[assembly: FlatcArguments("--cs-global-alias", "--gen-object-api")]
//[assembly: FlatcLocation("./flatc.exe")]
[assembly: FlatcReplaces("namespace FBS", "namespace Antelcat.FBS")]
[assembly: FlatcReplaces("global::FBS", "global::Antelcat.FBS")]
[assembly: FlatcReplaces("struct", "partial struct")]


class Test
{
    void Method()
    {
        var t = typeof(Antelcat.FBS.ActiveSpeakerObserver.ActiveSpeakerObserverOptions);
    }
}