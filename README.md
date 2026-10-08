# ResolutionX

Aplicativo Windows (WPF, .NET 10) para gerenciar resoluções de tela com teste seguro e
restauração automática. Este repositório está na **Fase 1**.

## Como executar (no Windows 10 ou 11)

Todos os comandos são digitados no **PowerShell** (menu Iniciar → digite "PowerShell" → Enter).

**1. Instale o .NET 10 SDK** (só na primeira vez):

```powershell
winget install Microsoft.DotNet.SDK.10
```

Feche e abra o PowerShell de novo. Para conferir se instalou:

```powershell
dotnet --version
```

Deve aparecer um número começando com `10.`.

**2. Entre na pasta do projeto.** Troque `E:` pela letra em que o HD aparece no seu Windows:

```powershell
cd "E:\ResolutionX"
```

**3. Compile:**

```powershell
dotnet build
```

No final deve aparecer `Compilação com êxito` / `Build succeeded` com `0 Erro(s)`.

**4. Execute:**

```powershell
dotnet run --project ResolutionX.App
```

Não há outras dependências: o projeto usa apenas o .NET SDK, sem pacotes NuGet.

## Como testar

1. **Detecção** — a lista MONITORES deve mostrar cada tela com GPU, resolução, Hz, conexão e escala.
2. **Diagnóstico** — clique em *Diagnóstico* e confira GPU, versão do driver e modos suportados.
3. **Teste que você confirma** — escolha uma resolução menor em "Resoluções suportadas",
   clique *TESTAR RESOLUÇÃO* e depois *SIM, MANTER*.
4. **Restauração automática** — teste outra resolução e **não clique em nada**: em 15 segundos
   a anterior deve voltar sozinha.
5. **Resolução recusada** — digite algo que o monitor não suporta (ex.: 5000 × 3000) e teste.
   Deve aparecer a mensagem sobre o Monitor Virtual, sem a tela piscar.
6. **Presets** — digite uma resolução, clique *Adicionar preset*, feche e reabra o app:
   ela deve continuar na lista. Selecione-a e clique *Excluir*.

## TESTAR x APLICAR

| Botão | O que faz |
| --- | --- |
| TESTAR RESOLUÇÃO | Troca só nesta sessão. Se confirmada, vale até reiniciar o PC. |
| APLICAR | Mesma confirmação de 15 s; ao confirmar, grava no Windows (permanente). |

Nos dois casos a resolução é primeiro validada com o driver e, depois de aplicada, lida de volta
para conferir. Um teste nunca é gravado no registro: no pior caso, reiniciar o PC desfaz.

## Estrutura

```
ResolutionX.App             Interface WPF (Views, ViewModels, diálogos)
ResolutionX.Core            Modelos, interfaces e serviços sem dependência do Windows
ResolutionX.Windows         P/Invoke e serviços reais: DisplayService, ResolutionService
ResolutionX.VirtualDisplay  VirtualDisplayService (contrato pronto; driver ainda não existe)
ResolutionX.Driver          Reservado para o Indirect Display Driver (ver README da pasta)
```

## O que a Fase 1 faz e não faz

Faz: detectar monitores, GPU e driver; listar e aplicar modos **que o driver já oferece**;
teste com timeout e restauração; presets; diagnóstico.

Ainda não faz: criar resoluções fora da lista do driver (Fase 2), ler EDID completo / número de
série (Fase 3), monitor virtual (Fases 4–5), escalonamento (Fase 6), perfis (Fase 7).
Os presets ficam em `%AppData%\ResolutionX\presets.json`.
