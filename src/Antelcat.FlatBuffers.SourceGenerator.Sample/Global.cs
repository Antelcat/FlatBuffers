using Antelcat.FlatBuffers;

[assembly: FlatcArguments("--cs-global-alias", "--gen-object-api")]
[assembly: FlatcLocation("./flatc.exe")]


file class Test
{
    Test()
    {
        var t = typeof(FBS.ActiveSpeakerObserver.ActiveSpeakerObserverOptions);
    }
}


