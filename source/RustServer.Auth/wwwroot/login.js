const form = document.querySelector('#login-form');
const signedIn = document.querySelector('#signed-in');
const status = document.querySelector('#status');
const submitButton = document.querySelector('#login-button');
const logoutButton = document.querySelector('#logout-button');

function setStatus(message, success = false) {
  status.textContent = message;
  status.classList.toggle('success', success);
}

// CSRF request token is tied to the current login identity.
async function csrfToken() {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('Could not prepare a secure request.');
  return (await response.json()).token;
}

async function refreshUser() {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin', cache: 'no-store' });
    if (response.ok) {
        window.location.replace('/portal');
        return;
    } else {
        signedIn.hidden = true;
        form.hidden = false;
    if (!query.has('googleError') && !query.has('steamError')) {
        setStatus('');
    }
  }
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  submitButton.disabled = true;
  setStatus('Signing in…');
  try {
    const token = await csrfToken();
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify({
        userName: document.querySelector('#username').value,
        password: document.querySelector('#password').value
      })
    });
    if (!response.ok) {
      setStatus(response.status === 401 ? 'Invalid credentials or account unavailable.' : 'Sign in failed. Please try again.');
      return;
    }
    document.querySelector('#password').value = '';
    await refreshUser();
  } catch {
    setStatus('Connection error. Check that Auth is running over HTTPS.');
  } finally {
    submitButton.disabled = false;
  }
});

logoutButton.addEventListener('click', async () => {
  logoutButton.disabled = true;
  try {
    const token = await csrfToken();
    const response = await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': token }
    });
    if (!response.ok) throw new Error('Sign out failed.');
    await refreshUser();
  } catch {
    setStatus('Sign out failed. Try again.');
  } finally {
    logoutButton.disabled = false;
  }
});

const query = new URLSearchParams(window.location.search);

if (query.has('googleError')) {
    setStatus('Google authentication failed or account is not linked.');
} else if (query.has('steamError')) {
    setStatus('Steam authentication failed. Please try again.');
}

refreshUser().catch(() => setStatus('Auth service is unavailable.'));

function redirectToPortal(user) {
    if (user.roles?.includes('Admin')) {
        window.location.replace('/admin');
    } else {
        window.location.replace('/player');
    }
}
