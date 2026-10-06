# HwOverlay

Overlay minimalista de monitoramento de hardware para Windows: um painel pequeno, sempre no topo,
com "relógios" (gauges) de temperatura e uso de CPU, GPU, RAM e placa-mãe.

- **Leitura de sensores:** [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6
- **Gauges:** [LiveCharts2](https://livecharts.dev) 2.0.5 (WPF / SkiaSharp)
- **UI:** WPF, .NET 8 (roda também com runtime 9/10 — `RollForward=Major`)

## Pré-requisitos

1. **.NET 8 SDK** (ou mais novo) / Visual Studio 2022+.
2. **Administrador.** O app pede elevação (UAC) ao abrir — temperaturas de CPU, placa-mãe e RAM exigem.
   Para depurar pelo Visual Studio, abra o VS como administrador.
3. **Driver PawnIO 2.0+.** O LibreHardwareMonitor trocou o antigo WinRing0 (hoje bloqueado pelo
   Windows Defender/antivírus) pelo PawnIO. Sem ele, várias temperaturas não aparecem.

   ```powershell
   winget install namazso.PawnIO
   ```

   ou baixe em <https://pawnio.eu>. A janela de sensores avisa se ele estiver faltando.

## Rodando

```powershell
cd HwOverlay
dotnet run --project src/HwOverlay
```

Ou abra `HwOverlay.sln` no Visual Studio (como administrador) e pressione F5.

## Como usar

1. Na primeira execução abre a janela **Sensores detectados**: é a árvore completa do que o
   LibreHardwareMonitor consegue ler na sua máquina (componente → tipo → sensor, com valor/mín/máx).
2. O overlay já começa com gauges **sugeridos** (CPU temp/uso, GPU temp/uso, RAM uso/temp, placa-mãe).
   Os nomes de sensores variam por fabricante, então confira na árvore se a escolha faz sentido.
3. **Duplo clique** num sensor (ou "Adicionar ao overlay") cria um gauge. No painel da direita dá para
   renomear, mudar a faixa (mín/máx), os limites de alerta (amarelo) e crítico (vermelho),
   o estilo (**Arco** ou **Ponteiro**), a ordem e remover.
4. **Copiar lista de sensores** copia tudo em texto (com os IDs) — útil para escolher os gauges.

### Overlay

| Ação | Como |
| --- | --- |
| Mover | arrastar com o botão esquerdo |
| Redimensionar gauges | Ctrl + roda do mouse (ou slider "Tamanho") |
| Menu | botão direito |
| Travar (click-through) | menu → "Travar"; o mouse passa a atravessar o overlay |
| Destravar / reabrir janelas / sair | ícone na bandeja do sistema |

O overlay funciona por cima de jogos em **tela cheia sem bordas / janela**. Em tela cheia
*exclusiva* o jogo cobre qualquer janela comum.

## Onde ficam as configurações

`%AppData%\HwOverlay\settings.json` — posição, aparência e gauges (cada gauge guarda o
`SensorId` do LHM, ex.: `/amdcpu/0/temperature/2`). Erros vão para `erros.log` na mesma pasta.
"Salvar relatório completo" grava o relatório técnico do LHM ali também.

## Estrutura

```
src/HwOverlay
├── App.xaml(.cs)                 composição: serviços, janelas, bandeja
├── Models/                       AppSettings/GaugeConfig (persistência) e snapshots imutáveis dos sensores
├── Services/
│   ├── HardwareMonitorService    LHM numa thread de fundo → MonitorSnapshot a cada ciclo
│   ├── DefaultGaugeSuggester     heurística dos gauges iniciais
│   ├── SensorFormatting          unidades, nomes de grupos, faixas padrão
│   ├── SettingsService           settings.json com gravação agrupada
│   ├── EnvironmentStatus         admin + versão do PawnIO
│   ├── OverlayWindowHelper       Win32: click-through, fora do Alt+Tab, topmost
│   └── TrayIconService           ícone da bandeja (WinForms NotifyIcon)
├── ViewModels/                   árvore de sensores, overlay e gauges (LiveCharts2)
└── Views/                        janela de sensores, overlay e o controle de gauge
```

A UI nunca toca nos objetos do LibreHardwareMonitor: o serviço atualiza o hardware numa thread
própria e entrega um snapshot imutável, que a UI aplica no dispatcher.
