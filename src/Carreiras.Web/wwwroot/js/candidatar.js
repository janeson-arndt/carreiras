// Formulário de candidatura: leva o foco ao resumo de erros e confere o currículo antes do envio.
// A validação de verdade é a do servidor; isto só poupa o envio de um arquivo grande.
(function () {
  'use strict';
  var resumo = document.getElementById('resumo-erros');
  if (resumo) resumo.focus();

  var campo = document.getElementById('Curriculo');
  var erro = document.getElementById('Curriculo-erro');
  if (!campo || !erro) return;

  campo.addEventListener('change', function () {
    var f = campo.files && campo.files[0];
    var msg = '';
    if (f) {
      if (!/\.(pdf|docx)$/i.test(f.name)) msg = 'Formato não aceito. Envie um arquivo PDF ou DOCX.';
      else if (f.size > 5 * 1024 * 1024) msg = 'O arquivo tem mais de 5 MB. Envie um arquivo menor.';
    }
    erro.textContent = msg;
    erro.hidden = !msg;
    campo.classList.toggle('is-invalid', !!msg);
    if (msg) { campo.setAttribute('aria-invalid', 'true'); campo.setAttribute('aria-describedby', 'Curriculo-erro'); }
    else { campo.removeAttribute('aria-invalid'); campo.setAttribute('aria-describedby', 'Curriculo-ajuda'); }
  });
})();
