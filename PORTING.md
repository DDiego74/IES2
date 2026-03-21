# Porting IES2 (IAW Scan 2) to .NET 10

## Panoramica

Questo documento descrive tutte le modifiche apportate per il porting del progetto **IES_2 (IAW Scan 2)** da **.NET Framework 2.0** a **.NET 10**, con aggiornamento di tutte le librerie esterne alle versioni più recenti.

L'applicazione è un tool diagnostico per centraline Marelli IAW (serie x6F/x8F/x8FD/04K) che comunica tramite interfaccia seriale (incluso adattatore Bluetooth OBD2) con la centralina del veicolo.

---

## Requisiti di sistema

- **Sistema operativo:** Windows 11 (o Windows 10 versione 1903+)
- **Runtime:** [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) per Windows (x64 o x86)
- **Hardware:** Adattatore OBD2 Bluetooth collegato a porta COM virtuale (es. COM3, COM4, ecc.)
- **Veicolo:** Fiat Panda con centralina Marelli IAW (IAW 16F, IAW 18F, IAW 18FD, IAW 8F.68, IAW 04K)

---

## Modifiche apportate

### 1. File di progetto: `IES_2/IES_2.csproj`

**Problema:** Il progetto utilizzava il formato `csproj` legacy (non SDK-style) targeting `.NET Framework 2.0` e faceva riferimento a una DLL di ZedGraph locale.

**Soluzione:** Conversione al formato **SDK-style** moderno con le seguenti impostazioni:

| Proprietà | Valore precedente | Valore nuovo |
|---|---|---|
| Formato progetto | Legacy MSBuild (ToolsVersion 15.0) | SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`) |
| Framework target | `net2.0` (TargetFrameworkVersion v2.0) | `net10.0-windows` |
| Windows Forms | Implicita (via reference) | `<UseWindowsForms>true</UseWindowsForms>` |
| EnableWindowsTargeting | N/A | `true` (necessario per cross-build su Linux/Mac) |
| SupportedOSPlatformVersion | N/A | `6.1` (Windows 7+) |
| GenerateAssemblyInfo | N/A | `false` (per mantenere `Properties/AssemblyInfo.cs` esistente) |
| NoWarn | N/A | `CA1416;CS8981` (soppressione warning attesi per app Windows-only) |

**Librerie NuGet aggiunte in sostituzione dei riferimenti legacy:**

| Pacchetto NuGet | Versione | Motivo |
|---|---|---|
| `ZedGraph` | 5.2.1 | Sostituisce la DLL locale ZedGraph 5.1.5 |
| `System.IO.Ports` | 10.0.5 | `SerialPort` non incluso nel runtime base di .NET Core/5+ |
| `System.Configuration.ConfigurationManager` | 10.0.5 | `ApplicationSettingsBase` non incluso nel runtime base di .NET Core/5+ |

I riferimenti legacy rimossi:
- `<Reference Include="System" />` → incluso automaticamente dall'SDK
- `<Reference Include="System.Data" />` → incluso automaticamente dall'SDK
- `<Reference Include="System.Deployment" />` → non disponibile/necessario in .NET 10
- `<Reference Include="System.Drawing" />` → incluso automaticamente dall'SDK (Windows Forms)
- `<Reference Include="System.Windows.Forms" />` → incluso tramite `<UseWindowsForms>true</UseWindowsForms>`
- `<Reference Include="System.Xml" />` → incluso automaticamente dall'SDK
- `<Reference Include="ZedGraph" HintPath="..\..\ZedGraph\ZedGraph.dll" />` → sostituito con NuGet
- Tutte le sezioni `<Compile>`, `<EmbeddedResource>`, `<None>` esplicite → gestite automaticamente dall'SDK
- Sezioni `<BootstrapperPackage>` → non più rilevanti in .NET 10

---

### 2. `IES_2/Program.cs`

**Problema:** Il file conteneva una workaround per `.NET Framework 2.0` che definiva `System.Runtime.CompilerServices.ExtensionAttribute` manualmente (necessario in .NET 2.0 per usare i metodi di estensione):

```csharp
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ExtensionAttribute : Attribute { }
}
```

**Soluzione:** Rimossa la definizione manuale dell'attributo. L'attributo `ExtensionAttribute` è nativo nel framework da .NET Framework 3.5 e disponibile in tutti i runtime .NET Core/.NET 5+.

---

### 3. `IES_2/Properties/AssemblyInfo.cs`

**Problema:** La versione dell'assembly usava un pattern con wildcard (`0.86.*`):

```csharp
[assembly: AssemblyVersion("0.86.*")]
```

**Soluzione:** I progetti SDK-style non supportano versioni con wildcard `*` di default (richiedono `<Deterministic>false</Deterministic>`). La versione è stata resa esplicita:

```csharp
[assembly: AssemblyVersion("0.86.0.0")]
[assembly: AssemblyFileVersion("0.86.0.0")]
```

---

### 4. `IES_2/AboutBox1.cs`

**Problema:** Utilizzo di `Assembly.CodeBase` (API obsoleta in .NET 5+):

```csharp
return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
```

**Soluzione:** Sostituito con `Assembly.Location` come raccomandato dalla documentazione Microsoft:

```csharp
return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
```

---

### 5. `IES_2.sln`

**Problema:** Il file di soluzione era nel formato Visual Studio 2017 (versione 15) con sezioni legacy.

**Soluzione:** Aggiornato al formato Visual Studio 2022 (versione 17), mantenendo gli stessi GUID di progetto e configurazioni di build. Rimossa la sezione `ExtensibilityGlobals` con il vecchio dato `BuildVersion_BuildVersioningStyle`.

---

## Librerie aggiornate

| Libreria | Versione precedente | Versione nuova | Note |
|---|---|---|---|
| .NET Framework | 2.0 | .NET 10.0 | Runtime LTS |
| ZedGraph | 5.1.5 (DLL locale) | 5.2.1 (NuGet) | Compatibile con .NET 6/8/10 |
| System.IO.Ports | (built-in .NET Fx 2.0) | 10.0.5 (NuGet) | `SerialPort` per comunicazione COM/Bluetooth |
| System.Configuration.ConfigurationManager | (built-in .NET Fx 2.0) | 10.0.5 (NuGet) | `ApplicationSettingsBase` per impostazioni utente |

---

## Utilizzo con adattatore OBD2 Bluetooth su Windows 11

### Configurazione Bluetooth OBD2

1. Collegare l'adattatore OBD2 alla presa diagnostica (OBD2/EOBD) della Fiat Panda.
2. Su Windows 11, aprire **Impostazioni → Bluetooth e dispositivi → Aggiungi dispositivo**.
3. Accoppiare l'adattatore OBD2 Bluetooth (di solito PIN: `1234` o `0000`).
4. Windows creerà automaticamente una porta COM virtuale (es. `COM3` o `COM4`).
   - Per verificare: **Gestione dispositivi → Porte (COM & LPT)**.
5. Avviare IES_2 e selezionare la porta COM corrispondente nelle impostazioni.

### Protocollo comunicazione

L'applicazione utilizza il protocollo **Marelli Passive Diagnostics** (non OBD2 standard):
- Baud rate di inizializzazione: **1200 baud**
- Baud rate comunicazione: **7680 baud**
- **Nota:** L'adattatore OBD2 deve supportare il protocollo ISO 9141-2 (K-line) a bassa velocità.

---

## Build e distribuzione

### Build da linea di comando

```bash
# Debug
dotnet build IES_2/IES_2.csproj

# Release
dotnet build IES_2/IES_2.csproj -c Release
```

### Pubblicazione self-contained per Windows

```bash
dotnet publish IES_2/IES_2.csproj -c Release -r win-x64 --self-contained true
```

Questo crea un eseguibile autonomo nella cartella `bin/Release/net10.0-windows/win-x64/publish/` che include il runtime .NET 10 e non richiede l'installazione separata del runtime.

### Prerequisiti per la macchina di destinazione

Se si sceglie un'installazione non self-contained, è necessario installare il **.NET 10 Desktop Runtime** (Windows):
- Download: https://dotnet.microsoft.com/en-us/download/dotnet/10.0

---

## Warning residui (non bloccanti)

| Codice | Descrizione | Stato |
|---|---|---|
| `CS8981` | I nomi di tipo `ecu`, `code`, `lang` contengono solo caratteri ASCII minuscoli | Soppressi (`NoWarn`). Si tratta di nomi originali del codice sorgente. |
| `CA1416` | API Windows Form reachable from all platforms | Soppressi (`NoWarn`). L'app è esplicitamente Windows-only (`net10.0-windows`). |

---

## Compatibilità

| Componente | Compatibilità |
|---|---|
| Windows 11 | ✅ Testato |
| Windows 10 (1903+) | ✅ Compatibile |
| Windows 7/8/8.1 | ⚠️ Non supportato (.NET 10 richiede Windows 10 1607+ per app Desktop) |
| Architettura x64 | ✅ |
| Architettura x86 | ✅ (AnyCPU) |
| Architettura ARM64 | ⚠️ Non testato, ma AnyCPU dovrebbe funzionare |
