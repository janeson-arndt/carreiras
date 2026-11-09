// Portal público: só melhorias progressivas. Sem JavaScript tudo continua funcionando (filtros por formulário GET).
(function () {
  // Filtros recolhidos no celular (a menos que já haja filtro aplicado), abertos no computador.
  var filtros = document.getElementById('filtros');
  if (filtros) {
    var ajusta = function () { filtros.open = window.innerWidth > 860 || filtros.hasAttribute('data-ativo'); };
    ajusta();
    window.addEventListener('resize', ajusta);
  }

  // O seletor de área aplica o filtro ao escolher (sem o botão "Aplicar", que só aparece sem JavaScript).
  var area = document.querySelector('[data-auto-envia]');
  if (area) area.addEventListener('change', function () { area.form.submit(); });
})();
