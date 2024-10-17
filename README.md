# Antelcat.FlatBuffers
 
Auto generate class from `.fbs` files

## Flatc download (Optional)

[Flatc binary](https://github.com/google/flatbuffers/releases/tag/v24.3.25)

## Usage

in `YourProject.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
    
    <ItemGroup>
        <!--add package-->
        <PackageReference Include="Antelcat.FlatBuffers" Version="*.*.*" />
        
        <!--add .fbs files-->
        <AdditionalFiles Include="{path}/{to}/{your}/{fbs}.fbs"/>
    </ItemGroup>

</Project>
```

add `.fbs` files in to tag `AdditionalFiles`

then it will automated generate `.cs` files.

## Owns flatc?
if you already have flatc in your device.

```csharp
[assembly:Antelcat.FlatBuffers.FlatcLocation("{path}/{to}/{your}/{flatc}")]
```

to use your local version of flatc