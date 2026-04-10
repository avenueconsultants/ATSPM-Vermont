import { ResponsivePageLayout } from '@/components/ResponsivePage'
import { useLogin } from '@/features/identity/api/getLogin'
import IdentityDto from '@/features/identity/types/identityDto'
import type { SsoProviderDefinition } from '@/features/identity/utils/ssoProviders'
import {
  loadVisibleSsoProviders,
  redirectToExternalLogin,
} from '@/features/identity/utils/ssoLogin'
import { setSecureCookie } from '@/features/identity/utils'
import { LoadingButton } from '@mui/lab'
import {
  Alert,
  Box,
  Button,
  Container,
  Divider,
  Grid,
  Link,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useState } from 'react'

function Login() {
  const [data, setData] = useState<IdentityDto>()
  const [email, setEmail] = useState<string>('')
  const [password, setPassword] = useState<string>('')
  const [errors, setErrors] = useState<string | null>(null)
  const [emailError, setEmailError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [visibleSsoProviders, setVisibleSsoProviders] = useState<SsoProviderDefinition[]>([])

  const {
    refetch,
    data: queryData,
    isLoading,
    error: queryDataError,
  } = useLogin({ email, password })

  useEffect(() => {
    if (queryData) {
      setData(queryData as IdentityDto)
    }
  }, [data, queryData])

  const validateEmail = (email: string) => {
    const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/
    return emailRegex.test(email)
  }

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setErrors(null)
    let isValid = true

    if (!email) {
      setEmailError('Email is required')
      isValid = false
    } else if (!validateEmail(email)) {
      setEmailError('Invalid email format')
      isValid = false
    } else {
      setEmailError(null)
    }

    if (!password) {
      setPasswordError('Password is required')
      isValid = false
    } else {
      setPasswordError(null)
    }

    if (isValid) {
      refetch()
    }
  }

  useEffect(() => {
    setEmailError(null)
    if (queryDataError) {
      setErrors('Invalid email or password')
    }
  }, [queryDataError, email])
  useEffect(() => {
    setPasswordError(null)
  }, [password])

  useEffect(() => {
    void loadVisibleSsoProviders().then(setVisibleSsoProviders)
  }, [])

  if (data?.code === 200) {
    setSecureCookie('token', data.token)
    setSecureCookie('claims', data.claims.join(','))
    setSecureCookie('loggedIn', 'True')
    window.location.href = '/'
  }

  return (
    <ResponsivePageLayout title={'login'} hideTitle>
      <Container component="main" maxWidth="xs">
        <Typography variant="h1" fontWeight="bold" sx={{ textAlign: 'center' }}>
          Login
        </Typography>
        <Box
          sx={{
            marginTop: 4,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
          }}
        >
          <Box
            component="form"
            noValidate
            onSubmit={handleSubmit}
            sx={{ mt: 3 }}
          >
            <Grid container spacing={2}>
              <Grid item xs={12}>
                <TextField
                  margin="normal"
                  required
                  fullWidth
                  id="email"
                  label="Email Address"
                  name="email"
                  autoComplete="email"
                  autoFocus
                  onChange={(e) => setEmail(e.target.value)}
                  error={!!emailError}
                  helperText={emailError}
                />
              </Grid>
              <Grid item xs={12}>
                <TextField
                  required
                  fullWidth
                  name="password"
                  label="Password"
                  type="password"
                  id="password"
                  autoComplete="new-password"
                  onChange={(e) => setPassword(e.target.value)}
                  error={!!passwordError}
                  helperText={passwordError}
                />
              </Grid>
            </Grid>
            {errors && (
              <Alert severity="error" sx={{ marginTop: '.5rem' }}>
                {errors}
              </Alert>
            )}
            {/* <Button
              type="submit"
              fullWidth
              variant="contained"
              sx={{ mt: 3, mb: 2 }}
            >
              Sign In
            </Button> */}
            <LoadingButton
              loading={isLoading}
              type="submit"
              fullWidth
              variant="contained"
              sx={{ mt: 3, mb: 2, p: 1 }}
            >
              Sign In
            </LoadingButton>
            {visibleSsoProviders.length > 0 && (
              <>
                <Divider>
                  <Typography variant="caption">or</Typography>
                </Divider>
                {visibleSsoProviders.map((provider) => (
                  <Button
                    key={provider.key}
                    variant="outlined"
                    sx={{ p: 1, mb: 1, mt: 1 }}
                    fullWidth
                    onClick={() => void redirectToExternalLogin(provider.key)}
                  >
                    <Box sx={{ width: '20px', height: '20px', mr: 1 }}></Box>
                    {provider.label}
                  </Button>
                ))}
              </>
            )}
            <Grid container justifyContent="space-between">
              <Grid item>
                <Link
                  style={{ color: 'cornflowerblue' }}
                  href="/password-reset"
                >
                  Forgot Password
                </Link>
              </Grid>
              <Grid item>
                <Link style={{ color: 'cornflowerblue' }} href="/register">
                  Dont have a Account? Sign Up
                </Link>
              </Grid>
            </Grid>
          </Box>
        </Box>
      </Container>
    </ResponsivePageLayout>
  )
}

export default Login
