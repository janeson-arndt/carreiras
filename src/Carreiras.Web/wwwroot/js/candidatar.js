// Formulário de candidatura: foco no resumo de erros, máscara do telefone, sugestão de cidades e conferência do currículo.
// A validação de verdade é a do servidor; isto só ajuda a pessoa a preencher e poupa o envio de um arquivo grande.
(function () {
  'use strict';
  var resumo = document.getElementById('resumo-erros');
  if (resumo) resumo.focus();

  // ---- Telefone: (47) 99999-9999 ou (47) 3333-4444 ----
  var tel = document.getElementById('Telefone');
  if (tel) {
    var mascarar = function (texto) {
      var d = (texto || '').replace(/\D/g, '');
      if (d.length > 11 && d.indexOf('55') === 0) d = d.slice(2); // colou com o código do país (+55)
      d = d.slice(0, 11);
      if (d.length === 0) return '';
      if (d.length <= 2) return '(' + d;
      var meio = d.length > 10 ? 7 : 6;
      if (d.length <= meio) return '(' + d.slice(0, 2) + ') ' + d.slice(2);
      return '(' + d.slice(0, 2) + ') ' + d.slice(2, meio) + '-' + d.slice(meio);
    };
    tel.addEventListener('input', function () { tel.value = mascarar(tel.value); });
    tel.value = mascarar(tel.value);
  }

  // ---- Pretensão salarial: R$ 12.000,00 (os dígitos entram da direita para a esquerda, como em máquina de cartão) ----
  var pret = document.getElementById('PretensaoSalarial');
  if (pret) {
    var moeda = function (digitos) {
      digitos = digitos.replace(/^0+/, '').slice(0, 11);
      if (!digitos) return '';
      while (digitos.length < 3) digitos = '0' + digitos;
      var reais = digitos.slice(0, -2).replace(/\B(?=(\d{3})+(?!\d))/g, '.');
      return 'R$ ' + reais + ',' + digitos.slice(-2);
    };
    pret.addEventListener('input', function () { pret.value = moeda(pret.value.replace(/\D/g, '')); });
    // valor que veio do servidor: "3.500,00" já traz os centavos; "3500" são reais inteiros
    if (pret.value) pret.value = moeda(/,/.test(pret.value) ? pret.value.replace(/\D/g, '') : pret.value.replace(/\D/g, '') + '00');
    pret.setAttribute('placeholder', 'R$ 0,00');
  }

  // ---- Cidade: lista de sugestões própria (combobox acessível) a partir da lista de municípios do próprio site ----
  var form = document.querySelector('[data-municipios]');
  var cidade = document.getElementById('Cidade');
  var uf = document.getElementById('Uf');
  var lista = document.getElementById('lista-cidades');
  if (form && cidade && uf && lista) {
    var municipios = null;   // { "SC": ["Indaial", ...], ... }
    var carregando = null;
    var ativo = -1;
    var itens = [];
    var semAcento = function (s) { return (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim(); };

    var carregar = function () {
      if (municipios) return Promise.resolve(municipios);
      if (!carregando) {
        carregando = fetch(form.getAttribute('data-municipios'), { credentials: 'same-origin' })
          .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
          .then(function (j) { municipios = j; return j; })
          .catch(function () { carregando = null; return null; }); // sem a lista, o campo segue como texto livre
      }
      return carregando;
    };

    var fechar = function () {
      lista.hidden = true; ativo = -1; itens = [];
      cidade.setAttribute('aria-expanded', 'false');
      cidade.removeAttribute('aria-activedescendant');
    };

    var marcar = function (i) {
      var lis = lista.children;
      if (ativo >= 0 && lis[ativo]) { lis[ativo].classList.remove('ativo'); lis[ativo].setAttribute('aria-selected', 'false'); }
      ativo = i;
      if (i >= 0 && lis[i]) {
        lis[i].classList.add('ativo'); lis[i].setAttribute('aria-selected', 'true');
        cidade.setAttribute('aria-activedescendant', lis[i].id);
        lis[i].scrollIntoView({ block: 'nearest' });
      } else cidade.removeAttribute('aria-activedescendant');
    };

    var escolher = function (i) {
      var it = itens[i];
      if (!it) return;
      cidade.value = it.nome;
      uf.value = it.uf;
      fechar();
    };

    var abrir = function (achados) {
      itens = achados; ativo = -1;
      lista.textContent = '';
      if (!achados.length) { fechar(); return; }
      achados.forEach(function (it, i) {
        var li = document.createElement('li');
        li.id = 'cidade-op-' + i;
        li.setAttribute('role', 'option');
        li.setAttribute('aria-selected', 'false');
        var nome = document.createElement('span'); nome.textContent = it.nome;
        var sigla = document.createElement('span'); sigla.className = 'uf'; sigla.textContent = it.uf;
        li.appendChild(nome); li.appendChild(sigla);
        li.addEventListener('mousedown', function (ev) { ev.preventDefault(); escolher(i); }); // antes do blur do campo
        lista.appendChild(li);
      });
      lista.hidden = false;
      cidade.setAttribute('aria-expanded', 'true');
    };

    // começa pelo nome digitado, depois os que o contêm; com estado escolhido só os dele
    var atualizar = function () {
      carregar().then(function (m) {
        if (!m) return;
        var q = semAcento(cidade.value);
        if (q.length < (uf.value ? 1 : 2)) { fechar(); return; }
        var inicio = [], meio = [];
        (uf.value && m[uf.value] ? [uf.value] : Object.keys(m)).forEach(function (s) {
          m[s].forEach(function (n) {
            var p = semAcento(n).indexOf(q);
            if (p === 0) inicio.push({ nome: n, uf: s }); else if (p > 0) meio.push({ nome: n, uf: s });
          });
        });
        abrir(inicio.concat(meio).slice(0, 8));
      });
    };

    // saiu do campo com uma cidade completa e sem estado: se o nome existe em um só estado, preenche o estado
    var deduzirUf = function () {
      if (uf.value || !municipios) return;
      var q = semAcento(cidade.value), onde = [];
      Object.keys(municipios).forEach(function (s) {
        if (municipios[s].some(function (n) { return semAcento(n) === q; })) onde.push(s);
      });
      if (onde.length === 1) uf.value = onde[0];
    };

    cidade.addEventListener('input', atualizar);
    cidade.addEventListener('focus', function () { if (cidade.value) atualizar(); });
    cidade.addEventListener('blur', function () { fechar(); carregar().then(deduzirUf); });
    uf.addEventListener('change', function () { if (document.activeElement === cidade) atualizar(); else fechar(); });
    cidade.addEventListener('keydown', function (ev) {
      if (lista.hidden) return;
      if (ev.key === 'ArrowDown') { ev.preventDefault(); marcar(ativo + 1 >= itens.length ? 0 : ativo + 1); }
      else if (ev.key === 'ArrowUp') { ev.preventDefault(); marcar(ativo <= 0 ? itens.length - 1 : ativo - 1); }
      else if (ev.key === 'Enter' && ativo >= 0) { ev.preventDefault(); escolher(ativo); }
      else if (ev.key === 'Escape') { ev.preventDefault(); fechar(); }
    });
    carregar(); // já deixa a lista pronta
  }

  // ---- Currículo ----
  var campo = document.getElementById('Curriculo');
  var erro = document.getElementById('Curriculo-erro');
  if (!campo || !erro) return;
  var mantido = document.getElementById('Curriculo-mantido');

  campo.addEventListener('change', function () {
    var f = campo.files && campo.files[0];
    var msg = '';
    if (f) {
      if (!/\.(pdf|docx)$/i.test(f.name)) msg = 'Formato não aceito. Envie um arquivo PDF ou DOCX.';
      else if (f.size > 5 * 1024 * 1024) msg = 'O arquivo tem mais de 5 MB. Envie um arquivo menor.';
    }
    erro.textContent = msg;
    erro.hidden = !msg;
    // escolheu outro arquivo: o aviso do arquivo mantido deixa de valer (o novo substitui o guardado)
    if (mantido) mantido.hidden = !!(f && !msg);
    campo.classList.toggle('is-invalid', !!msg);
    if (msg) { campo.setAttribute('aria-invalid', 'true'); campo.setAttribute('aria-describedby', 'Curriculo-erro'); }
    else { campo.removeAttribute('aria-invalid'); campo.setAttribute('aria-describedby', 'Curriculo-ajuda'); }
  });
})();
