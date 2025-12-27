document.addEventListener("DOMContentLoaded", () => {
  const form = document.getElementById("signupForm");
  const emailInput = document.getElementById("signupEmail");
  const passwordInput = document.getElementById("signupPassword");

  const reqs = {
    upper: /[A-Z]/,
    lower: /[a-z]/,
    number: /\d/,
    special: /[!@#$%^&*(),.?":{}|<>]/,
    length: /.{8,}/
  };

  const reqItems = {
    upper: document.getElementById("reqUpper"),
    lower: document.getElementById("reqLower"),
    number: document.getElementById("reqNumber"),
    special: document.getElementById("reqSpecial"),
    length: document.getElementById("reqLength")
  };

  // Password rule check (live)
  passwordInput.addEventListener("input", () => {
    const val = passwordInput.value;
    Object.entries(reqs).forEach(([key, regex]) => {
      if (regex.test(val)) reqItems[key].classList.add("valid");
      else reqItems[key].classList.remove("valid");
    });
  });

  // Submit handler
  form.addEventListener("submit", e => {
    e.preventDefault();
    const email = emailInput.value.trim();
    const password = passwordInput.value.trim();

    if (!email || !password) {
      showError(emailInput, "Required field");
      showError(passwordInput, "Required field");
      return;
    }

    const valid = Object.values(reqs).every(r => r.test(password));
    if (!valid) {
      showError(passwordInput, "Password must meet all requirements");
      return;
    }

    const users = JSON.parse(localStorage.getItem("users")) || [];

    if (users.some(u => u.username === email)) {
      showError(emailInput, "Account already exists");
      return;
    }

    users.push({ username: email, password });
    localStorage.setItem("users", JSON.stringify(users));

    alert("✅ Account created! Redirecting to login...");
    window.location.href = "login.html";
  });

  function showError(input, message) {
    const msg = input.nextElementSibling;
    msg.textContent = message;
    msg.style.display = "block";
    input.style.borderColor = "#e74c3c";
    setTimeout(() => {
      msg.style.display = "none";
      input.style.borderColor = "#ccc";
    }, 3000);
  }
});


document.getElementById("signupForm").addEventListener("submit", function (e) {
  e.preventDefault();

  const email = document.getElementById("signupEmail");
  const password = document.getElementById("signupPassword");

  let isValid = true;

  // Clear any old errors
  document.querySelectorAll(".field").forEach(field => field.classList.remove("error"));

  // Email validation
  if (!email.value) {
    setError(email, "Email is required");
    isValid = false;
  } else if (!/\S+@\S+\.\S+/.test(email.value)) {
    setError(email, "Enter a valid email address");
    isValid = false;
  }

  // Password validation
  if (!password.value) {
    setError(password, "Password is required");
    isValid = false;
  } else if (password.value.length < 8) {
    setError(password, "Password must be at least 8 characters");
    isValid = false;
  }

  if (isValid) {
    alert("✅ Account created successfully!");
    window.location.href = "login.html"; // Redirect to login
  }
});

function setError(input, message) {
  const field = input.closest(".field");
  const errorMessage = field.querySelector(".error-message");
  field.classList.add("error");
  errorMessage.textContent = message;
}

