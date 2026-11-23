document.addEventListener("DOMContentLoaded", function () {
  var box = document.getElementById("annualBox");
  if (!box) return;
  var boxes = box.querySelectorAll("input.decl");
  function submitButton() {
    return box.querySelector("input[type='submit'], button[type='submit']");
  }
  function sync() {
    var button = submitButton();
    if (!button) return;
    var ready = true;
    boxes.forEach(function (el) { if (!el.checked) ready = false; });
    button.disabled = !ready;
  }
  boxes.forEach(function (el) { el.addEventListener("change", sync); });
  sync();
});
