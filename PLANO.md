# Plano de desenvolvimento

## Objetivo

Reescrever o HwOverlay (C#/WPF, ~187 MB publicado) em C++ nativo.

| | C# atual | Meta C++ |
|---|---|---|
| Executável | ~187 MB (autocontido) | 300 KB a 1 MB |
| Memória em uso | runtime .NET + Skia | 5 a 15 MB |
| Dependências | .NET, LiveCharts, LibreHardwareMonitor | só APIs do Windows (+ PawnIO) |

## Princípios

- C++20, MSVC, CMake, sem frameworks (sem Qt). Win32 + Direct2D/DirectWrite. Link estático (`/MT`).
- O projeto C# continua existindo e serve de **referência**: comparar os valores lado a lado.
- Cobrir um **subconjunto** de sensores (o que existe na máquina de desenvolvimento e nos fabricantes principais).
  Não tentar igualar o LibreHardwareMonitor.
- A UI nunca bloqueia na leitura de sensores: thread de coleta publica um snapshot imutável, como no C#.

## Fora de escopo

- Sensores de placa-mãe e ventoinhas (Super I/O): centenas de chips, custo altíssimo.
- A janela com a árvore completa de sensores: sem um "tudo" para listar, vira arquivo de configuração
  + menu da bandeja com os sensores suportados.

## Fontes de dados

| Dado | API |
|---|---|
| Uso de CPU | `GetSystemTimes` |
| RAM | `GlobalMemoryStatusEx` |
| Bateria | `GetSystemPowerStatus` |
| Temperatura e potência da CPU Intel | PawnIO (biblioteca em C): MSR `IA32_PACKAGE_THERM_STATUS`, TjMax, RAPL |
| Uso de GPU (qualquer fabricante) | contadores PDH "GPU Engine" |
| Temperatura de GPU NVIDIA / AMD | NVML / ADL ou ADLX |
| Temperatura do NVMe | `IOCTL_STORAGE_QUERY_PROPERTY` |

CPU AMD: avaliar depois, só se houver máquina para testar.

## Fases

1. **Janela, bandeja e gauge de arco** com CPU e RAM por APIs do sistema. Janela em camadas sempre no topo,
   click-through, fora do Alt+Tab, `NotifyIcon` da bandeja, instância única.
2. **Modo barra de tarefas**: reaproveitar a lógica já validada no C# (ancoragem em `Shell_TrayWnd` →
   `TrayNotifyWnd`, topmost reafirmado, some em tela cheia e com a barra recolhida, paleta clara/escura pelo tema).
3. **Temperatura e potência da CPU via PawnIO.** Maior risco: confirmar cedo que o PawnIO funciona bem a partir de C++
   na máquina de desenvolvimento (i7-1255U). Exige administrador (manifesto `requireAdministrator`).
4. **GPU e NVMe** (PDH, NVML/ADL, IOCTL).
5. **Configuração e acabamento**: arquivo de configuração em `%AppData%\HwOverlay.Native`, menu da bandeja,
   iniciar com o Windows (tarefa agendada elevada com `--minimizado`, como no C#), estilo "ponteiro", fundo afundado.

As fases 1 e 2 já entregam um overlay útil.

## Riscos

- PawnIO a partir de C++ (fase 3): validar antes de investir nas fases seguintes.
- Cada família de CPU exige um módulo de leitura de MSR próprio.
- Desenho dos gauges em Direct2D: refazer a aparência hoje dada pelo LiveCharts/Skia.

## Pré-requisitos

- Visual Studio com a carga "Desenvolvimento para desktop com C++" (MSVC e CMake).
- Driver PawnIO 2.0+ (`winget install namazso.PawnIO`).
