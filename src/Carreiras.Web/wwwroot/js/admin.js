// Menus de ação (padrão disclosure): aria-expanded/aria-controls, Esc fecha e devolve o foco,
// clique fora fecha, só um aberto por vez, posição ajustada para não sair da tela.
(function () {
  function fechar(botao, devolverFoco) {
    var lista = document.getElementById(botao.getAttribute('aria-controls'));
    botao.setAttribute('aria-expanded', 'false');
    if (lista) lista.hidden = true;
    if (devolverFoco) botao.focus();
  }
  function fecharTodos(exceto) {
    document.querySelectorAll('[data-acoes][aria-expanded="true"]').forEach(function (b) {
      if (b !== exceto) fechar(b, false);
    });
  }
  function abrir(botao) {
    fecharTodos(botao);
    var lista = document.getElementById(botao.getAttribute('aria-controls'));
    botao.setAttribute('aria-expanded', 'true');
    lista.hidden = false;
    lista.style.left = ''; lista.style.right = '0';
    var r = lista.getBoundingClientRect();
    if (r.left < 8) { lista.style.right = 'auto'; lista.style.left = '0'; }
    var primeiro = lista.querySelector('button:not([aria-disabled="true"])');
    if (primeiro) primeiro.focus();
  }

  document.addEventListener('click', function (e) {
    var botao = e.target.closest('[data-acoes]');
    if (botao) {
      e.preventDefault();
      botao.getAttribute('aria-expanded') === 'true' ? fechar(botao, false) : abrir(botao);
      return;
    }
    var desativado = e.target.closest('button[aria-disabled="true"]');
    if (desativado) { e.preventDefault(); return; }
    if (!e.target.closest('.acoes-lista')) fecharTodos(null);
  });

  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    var aberto = document.querySelector('[data-acoes][aria-expanded="true"]');
    if (aberto) { e.preventDefault(); fechar(aberto, true); }
  });

  // Quando o foco sai do menu (Tab para fora), ele fecha: nunca fica um menu aberto sem foco.
  document.addEventListener('focusin', function (e) {
    if (!e.target.closest('.acoes')) fecharTodos(null);
  });

  // Confirmação antes de ações destrutivas (ex.: desativar).
  document.addEventListener('submit', function (e) {
    var msg = e.submitter && e.submitter.getAttribute('data-confirmar');
    if (msg && !window.confirm(msg)) e.preventDefault();
  });
})();
