(function () {
  var reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  var copy = {
    Owner: {
      title: "Welcome back",
      sub: "Sign in to manage sales, staff, and payroll for your business."
    },
    Employee: {
      title: "Team Portal",
      sub: "Sign in to clock in, view your payslips, and check your advanced payments."
    }
  };

  var title = document.getElementById("title");
  var subtitle = document.getElementById("subtitle");
  var ownerSection = document.getElementById("ownerSection");
  var employeeSection = document.getElementById("employeeSection");
  var empStep1 = document.getElementById("empStep1");
  var empStep2 = document.getElementById("empStep2");
  var employeeStep = document.getElementById("employeeStep");
  var employeeId = document.getElementById("employeeId");
  var empIdDisplay = document.getElementById("empIdDisplay");
  var employeePassword = document.getElementById("employeePassword");
  var ownerPassword = document.getElementById("ownerPassword");
  var submitBtn = document.getElementById("submitBtn");
  var submitBtnText = document.getElementById("submitBtnText");
  var backToIdBtn = document.getElementById("backToIdBtn");
  var form = document.getElementById("loginForm");

  function getSelectedRole() {
    var checked = document.querySelector('input[name="Input.Role"]:checked');
    return checked ? checked.value : "Owner";
  }

  function setRole(role) {
    [title, subtitle].forEach(function (el) { el && el.classList.add("swap"); });
    setTimeout(function () {
      if (title) title.textContent = copy[role].title;
      if (subtitle) subtitle.textContent = copy[role].sub;
      [title, subtitle].forEach(function (el) { el && el.classList.remove("swap"); });
    }, 180);

    if (role === "Owner") {
      ownerSection.classList.remove("hidden-section");
      employeeSection.classList.add("hidden-section");
      ownerPassword.required = true;
      employeeId.required = false;
      employeePassword.required = false;
      submitBtnText.textContent = "Sign in";
    } else {
      ownerSection.classList.add("hidden-section");
      employeeSection.classList.remove("hidden-section");
      ownerPassword.required = false;

      var step = parseInt(employeeStep.value, 10) || 1;
      if (step === 2) {
        showEmployeeStep2(employeeId.value);
      } else {
        showEmployeeStep1();
      }
    }
  }

  function showEmployeeStep1() {
    employeeStep.value = "1";
    empStep1.classList.remove("hidden-section");
    empStep2.classList.add("hidden-section");
    employeeId.required = true;
    employeePassword.required = false;
    employeePassword.value = "";
    submitBtnText.textContent = "Continue";
    setTimeout(function () { employeeId.focus(); }, 50);
  }

  function showEmployeeStep2(idVal) {
    employeeStep.value = "2";
    if (empIdDisplay) empIdDisplay.textContent = idVal || employeeId.value;
    empStep1.classList.add("hidden-section");
    empStep2.classList.remove("hidden-section");
    employeeId.required = false;
    employeePassword.required = true;
    submitBtnText.textContent = "Sign in";
    setTimeout(function () { employeePassword.focus(); }, 50);
  }

  // Handle Role Radio Toggle
  document.querySelectorAll('input[name="Input.Role"]').forEach(function (r) {
    r.addEventListener("change", function () {
      if (r.checked) setRole(r.value);
    });
  });

  // Handle Back Button in Step 2 to return to Employee ID input
  if (backToIdBtn) {
    backToIdBtn.addEventListener("click", function (e) {
      e.preventDefault();
      showEmployeeStep1();
    });
  }

  // Handle Form Submission: Client-side validation / step handling
  if (form) {
    form.addEventListener("submit", function (e) {
      var role = getSelectedRole();

      if (role === "Employee") {
        var step = parseInt(employeeStep.value, 10) || 1;
        if (step === 1) {
          var val = employeeId.value.trim();
          if (!val) {
            e.preventDefault();
            employeeId.focus();
            return;
          }
          // Advance to step 2: Password input appears
          e.preventDefault();
          showEmployeeStep2(val);
          return;
        }

        // In step 2, if password is blank, let browser validation handle it
        if (!employeePassword.value) {
          employeePassword.focus();
          return;
        }
      } else {
        // Owner role: only owner password
        if (!ownerPassword.value) {
          ownerPassword.focus();
          return;
        }
      }

      if (!form.checkValidity()) return;

      submitBtn.classList.add("loading");
      submitBtnText.textContent = "Signing in...";
      setTimeout(function () { submitBtn.disabled = true; }, 0);
    });
  }

  // Password visibility toggle for all eye buttons
  document.querySelectorAll(".eye").forEach(function (toggle) {
    toggle.addEventListener("click", function () {
      var targetId = toggle.getAttribute("data-target");
      var target = document.getElementById(targetId);
      if (!target) return;
      var show = target.type === "password";
      target.type = show ? "text" : "password";
      toggle.setAttribute("aria-label", show ? "Hide password" : "Show password");
    });
  });

  // Parallax effect on the photo panel
  var shell = document.getElementById("shell");
  if (!reduce && shell) {
    window.addEventListener("mousemove", function (e) {
      var x = (e.clientX / window.innerWidth - 0.5) * 2;
      var y = (e.clientY / window.innerHeight - 0.5) * 2;
      shell.style.setProperty("--mx", x.toFixed(3));
      shell.style.setProperty("--my", y.toFixed(3));
    }, { passive: true });
  }
})();
