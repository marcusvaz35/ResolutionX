# ResolutionX.Driver — Indirect Display Driver (Fase 5)

Esta pasta está reservada para o driver de monitor virtual. **Ainda não há código aqui**, e ela
não faz parte da solução `.slnx`, porque um driver não é compilado com `dotnet build`.

## Arquitetura correta (oficial da Microsoft)

- **Tipo de driver:** Indirect Display Driver (IDD), modelo **UMDF 2** com a biblioteca **IddCx**.
  Roda em *modo usuário* — não é driver de kernel e não usa nenhum hack.
- **Linguagem:** C++ (é o único caso do projeto em que C++ é necessário).
- **O que ele faz:** cria um adaptador virtual, "conecta" monitores com um EDID próprio e anuncia
  a lista de modos. O Windows passa a tratar cada um como um display independente.
- **Como o app conversa com ele:** o driver expõe uma interface de dispositivo; o
  `VirtualDisplayService` (C#) envia comandos por `DeviceIoControl`.

## O que será necessário no Windows

1. Visual Studio 2022 com a carga "Desenvolvimento para desktop com C++".
2. Windows SDK e **Windows Driver Kit (WDK)** da mesma versão.
3. Para testar: ativar *test signing* (`bcdedit /set testsigning on`) e reiniciar, ou usar uma
   máquina virtual. Isso exige administrador.
4. Para distribuir a outras pessoas: assinatura do driver pela Microsoft (Partner Center, com
   certificado EV). Sem isso o Windows recusa a instalação em máquinas comuns.

## Limitações a ter em mente

- Instalar/remover o driver sempre exige elevação de administrador.
- O monitor virtual não envia sinal a nenhum monitor físico: ele serve para captura (OBS),
  saída para software (Resolume), acesso remoto ou como origem para o escalonamento da Fase 6.
