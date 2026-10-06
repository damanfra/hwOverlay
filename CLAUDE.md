# HwOverlay — contexto para o Claude

Overlay minimalista de monitoramento de hardware para Windows (estilo HWMonitor, bem mais simples):
uma janela pequena, sempre no topo, com gauges ("relógios") de temperatura e uso de CPU, GPU, RAM e placa-mãe,
mais uma janela com a árvore completa de sensores para escolher o que vai no overlay.

Projeto pessoal. Textos de UI, comentários e commits em **português (pt-BR)**.

## Stack (versões verificadas)

- .NET 8, WPF (`net8.0-windows`, `RollForward=Major`), `UseWindowsForms` só para o `NotifyIcon` da bandeja
  (os usings implícitos `System.Windows.Forms`/`System.Drawing` foram removidos no .csproj para evitar ambiguidade).
- **LibreHardwareMonitorLib 0.9.6** — usa o driver **PawnIO 2.0+** (substituto do WinRing0, que hoje é bloqueado
  por antivírus). Instalação: `winget install namazso.PawnIO`. `LibreHardwareMonitor.PawnIo.PawnIo.Version` informa a versão instalada.
- **LiveChartsCore.SkiaSharpView.WPF 2.0.5** — gauges via `GaugeGenerator.BuildSolidGauge` (arco) e
  `GaugeGenerator.BuildAngularGaugeSections` + `NeedleVisual` + `AngularTicksVisual` (ponteiro).
- CommunityToolkit.Mvvm 8.4.0 (só `ObservableObject`/`RelayCommand`; propriedades escritas à mão, sem source generators).
- O app exige administrador (`app.manifest` → `requireAdministrator`). Para depurar, abrir o VS como admin.

## Arquitetura

```
src/HwOverlay
├── App.xaml(.cs)              composição: serviços, janelas, bandeja, instância única (Mutex)
├── Models/                    AppSettings/GaugeConfig (persistidos) e snapshots imutáveis (MonitorSnapshot)
├── Services/
│   ├── HardwareMonitorService LHM numa thread de fundo → publica MonitorSnapshot a cada ciclo
│   ├── DefaultGaugeSuggester  heurística dos gauges iniciais (com alternativas quando falta sensor)
│   ├── SensorFormatting       unidades, nomes de grupos pt-BR, faixas padrão por tipo
│   ├── SettingsService        %AppData%\HwOverlay\settings.json (gravação com debounce)
│   ├── EnvironmentStatus      admin + versão do PawnIO (aviso na janela de sensores)
│   ├── OverlayWindowHelper    Win32: click-through (WS_EX_TRANSPARENT), fora do Alt+Tab, reforço de topmost
│   ├── TaskbarLocator         Win32: retângulo da barra de tarefas e da bandeja, ocultação automática, tela cheia
│   └── TrayIconService        ícone da bandeja (porta de volta quando o overlay está travado)
├── ViewModels/                TreeNodes (árvore), SensorTreeViewModel, OverlayViewModel, GaugeViewModel
├── Views/                     SensorTreeWindow, OverlayWindow, GaugeView,
│                              TaskbarWindow + MiniArc (modo ultra compacto sobre a barra de tarefas)
└── Themes/Dark.xaml           paleta e estilos (fundo #12161C, destaque #3DDC97)
```

Regras importantes:
- **A UI nunca toca nos objetos do LHM.** O serviço atualiza o hardware sob lock na thread dele e entrega um
  `MonitorSnapshot` imutável; a UI aplica via `Dispatcher.InvokeAsync`.
- A árvore só é reconstruída quando `MonitorSnapshot.StructureKey` muda; nos demais ciclos só os valores são atualizados.
- `sensor.ValuesTimeWindow = TimeSpan.Zero` em todos os sensores (o padrão do LHM guarda 24h de histórico por sensor → centenas de MB).
- Gauges guardam o `SensorId` do LHM (ex.: `/intelcpu/0/temperature/12`).
- LiveCharts2: propriedades setadas por código (Fill, CornerRadius, DataLabelsPaint = null...) não são sobrescritas pelo tema
  (`AddDarkTheme`) — o tema só preenche o que não foi setado. O valor do gauge é um `TextBlock` WPF, não data label.
- Gauge "Arco": gráfico vai de 0 a (max − min) e o valor é deslocado; "Ponteiro": MinValue/MaxValue reais, seções relativas ao mínimo.
- Pixels 100% transparentes não recebem clique em janela WPF transparente → fundo do overlay tem alfa mínimo 1/255 para permitir arrastar.

## Máquina de desenvolvimento (sensores reais)

Notebook Positivo (placa DN50E-140H_I219V), Intel Core i7-1255U, GPU Intel Iris Xe integrada, 2× DDR4 A-DATA 8 GB, NVMe CL4-8D512.
- **Não existem**: temperatura de GPU (iGPU no mesmo chip da CPU), temperatura de RAM (pentes sem sensor),
  sensores de placa-mãe (nó `/motherboard` vazio — notebook usa EC, não Super I/O legível).
- IDs úteis: CPU Package temp `/intelcpu/0/temperature/12`, CPU Total `/intelcpu/0/load/0`,
  CPU Package power `/intelcpu/0/power/0`, RAM física `/ram/load/0`, SSD `/nvme/0/temperature/0`,
  GPU uso "D3D 3D" `/gpu-intel-integrated/.../load/7`, bateria `/battery/.../level/0`.
- Atenção: existem dois sensores "Memory" — `/ram` (RAM física) e `/vram` (memória virtual = RAM + paginação).
  O gauge "RAM Uso" deve usar `/ram/load/0`.

## Histórico

1. Estrutura inicial: árvore de sensores + overlay com gauges sugeridos. Compilou e rodou na primeira tentativa.
2. Correções após o primeiro teste: sugestão de RAM pegava `/vram` (corrigido); alternativas automáticas
   (CPU Potência no lugar de GPU Temp em iGPU, SSD Temp no lugar de placa-mãe); botão "Restaurar sugestões";
   nomes claros para memória física/virtual/pentes na árvore; texto da opção "Travar" encurtado.
3. Branch `feature/modo-barra-tarefas` — protótipo do modo ultra compacto (opção 1 de 3 discutidas):
   janela sobreposta à barra (o Win11 não tem API de DeskBand), posicionada em pixels físicos via `SetWindowPos`,
   ancorada na bandeja (`Shell_TrayWnd` → `TrayNotifyWnd`) ou na borda esquerda, com distância ajustável (arrastar).
   Reafirma topmost a cada 500 ms e quando a barra vira foreground (`SetWinEventHook`); some em tela cheia
   (`SHQueryUserNotificationState`) e com a barra recolhida (ocultação automática). Só barra horizontal.
   Primeiro teste aprovado (capturas/captura01.png). Depois: fundo próprio (`TaskbarBackgroundOpacity`, padrão 0%)
   e paleta clara/escura (`TaskbarPalette`) conforme o tema da barra — a barra desta máquina é clara
   (`SystemUsesLightTheme = 1`), e texto branco sem fundo sumia (captura02.png).

## Pendências / ideias

- Modo barra de tarefas: testar visualmente; depois as outras opções — ícones vivos na bandeja (valor desenhado
  no ícone) e `TaskbarItemInfo` (selo/progresso no ícone do app). Ainda falta: vários monitores, barra vertical (Win10),
  tema claro da barra.
- Conferir visualmente o estilo "Ponteiro" (tamanhos de marcações calculados sem teste visual).
- Talvez: iniciar com o Windows, atalho global para travar/destravar, mini-gráfico de histórico (sparkline) nos gauges,
  temas de cor, publicar como single-file (`dotnet publish -r win-x64 --self-contained`).
